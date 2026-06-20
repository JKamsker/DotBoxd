using System.Buffers;
using System.Reflection;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage.SubServices;

public sealed class DispatcherSubServiceReleaseAsyncTests
{
    private const string Source = """
        using DotBoxD.Services.Attributes;
        using System;
        using System.Threading.Tasks;

        namespace Coverage.SubServiceRelease
        {
            [DotBoxDService]
            public interface ISubService : IAsyncDisposable
            {
                Task<int> CountAsync();
            }

            [DotBoxDService]
            public interface IRootService
            {
                Task<ISubService> GetSubAsync(string label);
            }
        }
        """;

    [Fact]
    public void Dispatcher_AwaitsRegisteredSubServiceRelease_WhenResponseSerializationFails()
    {
        var completed = PumpRunner.RunWithTimeout(async () =>
        {
            var asm = Compile(Source);
            var dispatcherType = asm.GetType("Coverage.SubServiceRelease.RootServiceDispatcher")!;
            var iRoot = asm.GetType("Coverage.SubServiceRelease.IRootService")!;
            var iSub = asm.GetType("Coverage.SubServiceRelease.ISubService")!;

            var disposal = new AsyncDisposalGate();
            var subImpl = SubImplFactory.Create(iSub, disposal);
            var rootImpl = RootImplFactory.Create(iRoot, _ => subImpl);
            var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType, rootImpl)!;
            var registry = new InstanceRegistry();
            var serializer = new ThrowingServiceHandleSerializer();
            using var payload = serializer.SerializeToPayload("hello");

            _ = Task.Run(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                disposal.AllowCompletion();
            });

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.DispatchToPayloadAsync(
                    "GetSubAsync",
                    payload.Memory,
                    serializer,
                    registry,
                    CancellationToken.None));

            Assert.True(disposal.Completed);
        }, TimeSpan.FromSeconds(5));

        Assert.True(
            completed,
            "dispatcher cleanup sync-blocked Release on an async sub-service disposer instead of awaiting it");
    }

    private static Assembly Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var final = ((CSharpCompilation)compilation).AddSyntaxTrees(driver.GetRunResult().GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        Assert.True(
            emit.Success,
            string.Join(
                "\n",
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        return Assembly.Load(ms.ToArray());
    }

    private sealed class ThrowingServiceHandleSerializer : ISerializer
    {
        private readonly TestJsonSerializer _inner = new();

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            if (value is ServiceHandle)
            {
                throw new InvalidOperationException("service handle serialization failed");
            }

            _inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => _inner.Deserialize<T>(data);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => _inner.Deserialize(data, type);
    }

    public sealed class AsyncDisposalGate
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Completed { get; private set; }

        public void AllowCompletion() => _completion.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            await _completion.Task;
            Completed = true;
        }
    }

    private static class SubImplFactory
    {
        public static object Create(Type subIface, AsyncDisposalGate disposal)
        {
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(subIface, typeof(SubStub));
            var proxy = closed.Invoke(null, null)!;
            ((SubStub)proxy).Disposal = disposal;
            return proxy;
        }
    }

    public class SubStub : DispatchProxy
    {
        public AsyncDisposalGate? Disposal;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "DisposeAsync")
            {
                return Disposal!.DisposeAsync();
            }

            if (targetMethod?.Name == "CountAsync")
            {
                return Task.FromResult(0);
            }

            throw new InvalidOperationException("unexpected " + targetMethod?.Name);
        }
    }

    private static class RootImplFactory
    {
        public static object Create(Type rootIface, Func<string, object> mintSub)
        {
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(rootIface, typeof(RootStub));
            var proxy = closed.Invoke(null, null)!;
            ((RootStub)proxy).Mint = mintSub;
            return proxy;
        }
    }

    public class RootStub : DispatchProxy
    {
        public Func<string, object>? Mint;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetSubAsync")
            {
                var sub = Mint!((string)args![0]!);
                var iSub = targetMethod.ReturnType.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(iSub)
                    .Invoke(null, [sub]);
            }

            throw new InvalidOperationException("unexpected " + targetMethod?.Name);
        }
    }

    private static class PumpRunner
    {
        public static bool RunWithTimeout(Func<Task> asyncMethod, TimeSpan timeout)
        {
            using var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                var context = new PumpSyncContext();
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    var root = asyncMethod();
                    _ = root.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
                    context.PumpUntilComplete();
                    if (root.IsCompletedSuccessfully)
                    {
                        done.Set();
                    }
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            })
            {
                IsBackground = true,
                Name = "sub-service-release-pump",
            };

            thread.Start();
            return done.Wait(timeout);
        }
    }

    private sealed class PumpSyncContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly AutoResetEvent _available = new(false);
        private bool _completed;

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
            {
                if (_completed)
                {
                    return;
                }

                _queue.Enqueue((d, state));
            }

            _available.Set();
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void Complete()
        {
            lock (_queue)
            {
                _completed = true;
            }

            _available.Set();
        }

        public void PumpUntilComplete()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) work;
                lock (_queue)
                {
                    if (_queue.Count > 0)
                    {
                        work = _queue.Dequeue();
                    }
                    else if (_completed)
                    {
                        return;
                    }
                    else
                    {
                        work = default;
                    }
                }

                if (work.Callback is null)
                {
                    _available.WaitOne();
                    continue;
                }

                work.Callback(work.State);
            }
        }
    }
}
