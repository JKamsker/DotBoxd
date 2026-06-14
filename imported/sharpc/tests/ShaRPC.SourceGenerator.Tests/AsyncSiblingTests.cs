using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Client;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Coverage for the auto-generated async sibling interface. For every
/// <c>[ShaRpcService]</c> interface the generator emits a sibling
/// <c>I{Name}Async</c> whose members are non-blocking. The proxy class
/// implements both interfaces.
/// </summary>
public class AsyncSiblingTests
{
    /// <summary>
    /// A sync method on the user interface produces an async counterpart on the sibling,
    /// and the proxy exposes both: the blocking original (returning T) and the awaitable
    /// sibling (returning Task&lt;T&gt;).
    /// </summary>
    [Fact]
    public async Task SyncMethod_GeneratesAsyncSibling_AndProxyImplementsBothInterfaces()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace AsyncSibling.A
            {
                [ShaRpcService]
                public interface ICalc
                {
                    int Add(int a, int b);
                }
            }
            """;

        var (asm, _) = Compile(source);

        var sync = asm.GetType("AsyncSibling.A.ICalc")!;
        var async = asm.GetType("AsyncSibling.A.ICalcAsync");
        async.Should().NotBeNull("the generator must emit a sibling interface named ICalcAsync");

        var proxy = asm.GetType("AsyncSibling.A.CalcProxy")!;
        sync.IsAssignableFrom(proxy).Should().BeTrue();
        async!.IsAssignableFrom(proxy).Should().BeTrue("the proxy must implement both views");

        // The async sibling's AddAsync method must accept (int, int, CancellationToken).
        var siblingAdd = async.GetMethod("AddAsync")!;
        siblingAdd.ReturnType.Should().Be(typeof(Task<int>));
        siblingAdd.GetParameters().Select(p => p.ParameterType)
            .Should().BeEquivalentTo(new[] { typeof(int), typeof(int), typeof(CancellationToken) },
                "the sibling appends a CancellationToken with default value");

        // Sanity: at runtime the awaitable sibling call goes through the same wire path
        // as the blocking original.
        var recorder = new Recorder { NextResult = 42 };
        var instance = Activator.CreateInstance(proxy, recorder)!;
        var task = (Task<int>)siblingAdd.Invoke(instance, new object[] { 4, 5, CancellationToken.None })!;
        (await task).Should().Be(42);
        recorder.LastService.Should().Be("ICalc");
        recorder.LastMethod.Should().Be("Add");
    }

    /// <summary>
    /// A method whose original name already ends in <c>Async</c> and is already async with
    /// a CT parameter must NOT produce a duplicate proxy method — one implementation
    /// satisfies both interfaces.
    /// </summary>
    [Fact]
    public void AlreadyAsyncWithCt_DoesNotCauseDuplicateProxyMethod()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.B
            {
                [ShaRpcService]
                public interface IAlready
                {
                    Task<int> FooAsync(int x, CancellationToken ct = default);
                }
            }
            """;

        var (asm, _) = Compile(source);
        var proxy = asm.GetType("AsyncSibling.B.AlreadyProxy")!;
        var fooMethods = proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FooAsync").ToArray();
        fooMethods.Should().HaveCount(1,
            "a single physical method already satisfies both IAlready and IAlreadyAsync");
    }

    /// <summary>
    /// An already-async method WITHOUT a CT must get a sibling method that adds a CT
    /// — so the proxy emits TWO physical methods.
    /// </summary>
    [Fact]
    public void AsyncWithoutCt_GeneratesSecondProxyMethodWithCt()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace AsyncSibling.C
            {
                [ShaRpcService]
                public interface INoCt
                {
                    Task<int> FetchAsync(int id);
                }
            }
            """;

        var (asm, _) = Compile(source);
        var proxy = asm.GetType("AsyncSibling.C.NoCtProxy")!;
        var fetchMethods = proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync").ToArray();
        fetchMethods.Should().HaveCount(2);
        fetchMethods.Should().Contain(m => m.GetParameters().Length == 1, "the original interface method");
        fetchMethods.Should().Contain(m => m.GetParameters().Length == 2
            && m.GetParameters()[1].ParameterType == typeof(CancellationToken),
            "the sibling adds CancellationToken");
    }

    /// <summary>
    /// When the user interface has a sync method called <c>Foo</c> AND an async method
    /// called <c>FooAsync</c> with the same parameters, projecting <c>Foo</c> onto its
    /// sibling form <c>FooAsync</c> would collide. The generator surfaces SHARPC004
    /// and skips the colliding row.
    /// </summary>
    [Fact]
    public void SyncMethodColliding_WithExistingAsyncName_FiresSHARPC004()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace AsyncSibling.D
            {
                [ShaRpcService]
                public interface IClash
                {
                    int Add(int a, int b);
                    Task<int> AddAsync(int a, int b);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var diags = driver.GetRunResult().Diagnostics;

        diags.Should().Contain(d => d.Id == "SHARPC004",
            "the sync 'Add' would project to 'AddAsync', which is already declared");
        diags.Where(d => d.Id == "SHARPC004")
            .Should().OnlyContain(d => d.Location != Location.None);
    }

    /// <summary>
    /// When a projected sync method collides with a real async method that already has
    /// the sibling signature, the real method should satisfy the sibling and the proxy
    /// must not emit a duplicate extra method.
    /// </summary>
    [Fact]
    public void SyncProjectionColliding_WithExistingAsyncCtMethod_DoesNotEmitDuplicateProxyMethod()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.H
            {
                [ShaRpcService]
                public interface IClashCt
                {
                    int Add(int x);
                    Task<int> AddAsync(int x, CancellationToken ct = default);
                }
            }
            """;

        var (asm, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC004");

        var proxy = asm.GetType("AsyncSibling.H.ClashCtProxy")!;
        var addAsyncMethods = proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "AddAsync")
            .Where(m =>
            {
                var parameters = m.GetParameters();
                return parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(int) &&
                    parameters[1].ParameterType == typeof(CancellationToken);
            })
            .ToArray();
        addAsyncMethods.Should().ContainSingle(
            "the original AddAsync implementation should satisfy the sibling signature");
    }

    [Fact]
    public void AsyncProjectionColliding_WithExistingAsyncCtOverload_FiresSHARPC004()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.J
            {
                [ShaRpcService]
                public interface IAsyncClash
                {
                    [ShaRpcMethod(Name = "FetchNoCt")]
                    Task<int> FetchAsync(int value);
                    [ShaRpcMethod(Name = "FetchWithCt")]
                    Task<int> FetchAsync(int value, CancellationToken ct = default);
                }
            }
            """;

        var (asm, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC004" &&
            d.GetMessage().Contains("FetchAsync"));

        var proxy = asm.GetType("AsyncSibling.J.AsyncClashProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync")
            .Where(m => m.GetParameters().Length == 2 &&
                m.GetParameters()[1].ParameterType == typeof(CancellationToken))
            .Should().ContainSingle();
    }

    /// <summary>
    /// The generated CancellationToken parameter on a sibling method must avoid names
    /// already used by payload parameters.
    /// </summary>
    [Fact]
    public void GeneratedSiblingCancellationTokenParameter_AvoidsUserParameterNameCollision()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace AsyncSibling.I
            {
                [ShaRpcService]
                public interface INameCollision
                {
                    int Echo(int ct);
                }
            }
            """;

        var (_, runResult) = Compile(source);

        var generated = runResult.Results.Single().GeneratedSources;
        var sibling = generated
            .Single(g => g.HintName == "AsyncSibling_I_INameCollision.ShaRpcAsync.g.cs")
            .SourceText.ToString();
        sibling.Should().Contain(
            "EchoAsync(int ct, global::System.Threading.CancellationToken ct1 = default);");

        var proxy = generated
            .Single(g => g.HintName == "AsyncSibling_I_INameCollision.ShaRpcProxy.g.cs")
            .SourceText.ToString();
        proxy.Should().Contain(
            "EchoAsync(int ct, global::System.Threading.CancellationToken ct1 = default)");
        proxy.Should().Contain(
            "InvokeAsync<int, int>(\"INameCollision\", \"Echo\", ct, ct1)");
    }

    /// <summary>
    /// The async sibling source file is emitted under its own hint name and the proxy
    /// references the sibling by its fully-qualified name, so it compiles even when
    /// dropped into a project with overlapping using imports.
    /// </summary>
    [Fact]
    public void SiblingInterfaceFile_IsEmittedUnderSiblingHintName()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace AsyncSibling.E
            {
                [ShaRpcService]
                public interface IThing
                {
                    int Compute(int x);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var hints = driver.GetRunResult().Results.Single().GeneratedSources
            .Select(g => g.HintName).OrderBy(h => h).ToArray();
        hints.Should().Contain("AsyncSibling_E_IThing.ShaRpcAsync.g.cs",
            "the sibling interface file goes under its own .ShaRpcAsync.g.cs hint name");
    }

    /// <summary>
    /// A service interface whose own name already ends in <c>Async</c> would collide
    /// with the generated sibling type name, so the generator must skip the sibling
    /// and still emit compilable proxy/dispatcher code for the service itself.
    /// </summary>
    [Fact]
    public void ServiceInterfaceNameEndingInAsync_DoesNotEmitDuplicateSiblingType_AndCompiles()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace AsyncSibling.G
            {
                [ShaRpcService]
                public interface IFooAsync
                {
                    Task<int> GetAsync();
                }
            }
            """;

        var (asm, runResult) = Compile(source);

        var service = asm.GetType("AsyncSibling.G.IFooAsync")!;
        var proxy = asm.GetType("AsyncSibling.G.FooAsyncProxy")!;
        service.IsAssignableFrom(proxy).Should().BeTrue();

        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("AsyncSibling_G_IFooAsync.ShaRpcProxy.g.cs");
        hints.Should().Contain("AsyncSibling_G_IFooAsync.ShaRpcDispatcher.g.cs");
        hints.Should().NotContain("AsyncSibling_G_IFooAsync.ShaRpcAsync.g.cs",
            "the generated sibling type would have the same name as the user service interface");
    }

    /// <summary>
    /// Calling the async sibling method on the proxy must not block — the underlying
    /// IRpcInvoker call uses the awaited path, not GetAwaiter().GetResult().
    /// </summary>
    [Fact]
    public async Task SiblingCall_IsTrulyNonBlocking_NotAGetResultWrapper()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace AsyncSibling.F
            {
                [ShaRpcService]
                public interface IBlocker
                {
                    int Slow(int x);
                }
            }
            """;

        var (asm, _) = Compile(source);

        // The recorder's invocation returns an unstarted task and only completes when
        // we explicitly signal it — so a blocking GetAwaiter().GetResult() would deadlock
        // the test, but a true async path would let us await it.
        var gate = new TaskCompletionSource<object?>();
        var recorder = new DeferredRecorder(gate.Task);
        var proxy = asm.GetType("AsyncSibling.F.BlockerProxy")!;
        var instance = Activator.CreateInstance(proxy, recorder)!;
        var siblingMethod = asm.GetType("AsyncSibling.F.IBlockerAsync")!.GetMethod("SlowAsync")!;

        // Kick off the sibling call without awaiting it; release the gate after a short
        // delay; then await the task. If the sibling were secretly blocking, the test
        // would deadlock on a single-threaded sync context — but the test pool has
        // multiple threads, so we time-bound the await as a backstop.
        var task = (Task<int>)siblingMethod.Invoke(instance, new object[] { 7, CancellationToken.None })!;
        task.IsCompleted.Should().BeFalse("the sibling call must not have synchronously completed");
        gate.SetResult(99);
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(task, "the sibling call must complete after the underlying client task does");
        (await task).Should().Be(99);
    }

    // ---- helpers ----

    private static (Assembly Assembly, GeneratorDriverRunResult RunResult) Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var final = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            var dump = string.Join("\n----\n", final.SyntaxTrees.Select(t =>
                t.FilePath + "\n" + t.GetText().ToString()));
            throw new InvalidOperationException("Emit failed: " + errors + "\n\n" + dump);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("AsyncSibling_" + Guid.NewGuid(), isCollectible: false);
        return (alc.LoadFromStream(ms), runResult);
    }

    private sealed class Recorder : global::ShaRPC.Core.IRpcInvoker
    {
        public object? NextResult;
        public string? LastService { get; private set; }
        public string? LastMethod { get; private set; }

        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<TR> InvokeAsync<TQ, TR>(string svc, string method, TQ req, CancellationToken ct = default)
        {
            LastService = svc;
            LastMethod = method;
            return Task.FromResult((TR)NextResult!);
        }
        public Task<TR> InvokeAsync<TR>(string svc, string method, CancellationToken ct = default)
        {
            LastService = svc;
            LastMethod = method;
            return Task.FromResult((TR)NextResult!);
        }
        public Task InvokeAsync<TQ>(string svc, string method, TQ req, CancellationToken ct = default)
        {
            LastService = svc;
            LastMethod = method;
            return Task.CompletedTask;
        }
        public Task InvokeAsync(string svc, string method, CancellationToken ct = default)
        {
            LastService = svc;
            LastMethod = method;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => default;

        // The Feature-2 sub-service overloads aren't exercised by these tests, but the
        // interface requires them — forward to the singleton variants so any accidental
        // sub-routed call still records and returns sensibly.
        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(string svc, string id, string method, TQ req, CancellationToken ct = default)
            => InvokeAsync<TQ, TR>(svc, method, req, ct);
        public Task<TR> InvokeOnInstanceAsync<TR>(string svc, string id, string method, CancellationToken ct = default)
            => InvokeAsync<TR>(svc, method, ct);
        public Task InvokeOnInstanceAsync<TQ>(string svc, string id, string method, TQ req, CancellationToken ct = default)
            => InvokeAsync(svc, method, req, ct);
        public Task InvokeOnInstanceAsync(string svc, string id, string method, CancellationToken ct = default)
            => InvokeAsync(svc, method, ct);
    }

    /// <summary>
    /// A client whose response task only completes when the supplied gate task does —
    /// used to prove the sibling proxy never blocks the caller.
    /// </summary>
    private sealed class DeferredRecorder : global::ShaRPC.Core.IRpcInvoker
    {
        private readonly Task<object?> _gate;
        public DeferredRecorder(Task<object?> gate) { _gate = gate; }

        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<TR> InvokeAsync<TQ, TR>(string svc, string method, TQ req, CancellationToken ct = default)
        {
            var v = await _gate.ConfigureAwait(false);
            return (TR)v!;
        }
        public async Task<TR> InvokeAsync<TR>(string svc, string method, CancellationToken ct = default)
        {
            var v = await _gate.ConfigureAwait(false);
            return (TR)v!;
        }
        public Task InvokeAsync<TQ>(string svc, string method, TQ req, CancellationToken ct = default) =>
            _gate;
        public Task InvokeAsync(string svc, string method, CancellationToken ct = default) =>
            _gate;
        public ValueTask DisposeAsync() => default;

        // The Feature-2 sub-service overloads aren't exercised by these tests, but the
        // interface requires them — forward to the singleton variants so any accidental
        // sub-routed call still records and returns sensibly.
        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(string svc, string id, string method, TQ req, CancellationToken ct = default)
            => InvokeAsync<TQ, TR>(svc, method, req, ct);
        public Task<TR> InvokeOnInstanceAsync<TR>(string svc, string id, string method, CancellationToken ct = default)
            => InvokeAsync<TR>(svc, method, ct);
        public Task InvokeOnInstanceAsync<TQ>(string svc, string id, string method, TQ req, CancellationToken ct = default)
            => InvokeAsync(svc, method, req, ct);
        public Task InvokeOnInstanceAsync(string svc, string id, string method, CancellationToken ct = default)
            => InvokeAsync(svc, method, ct);
    }
}
