using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Round1 regression test for DEFECT #2: the generated sub-service dispatch case has no
/// try/catch around <c>registry.Register(...)</c>. When the per-connection
/// <see cref="InstanceRegistry"/> is at capacity, <c>Register</c> throws and the already-created
/// sub-service instance (<c>var __sub = await call;</c>) is leaked — it is never disposed even
/// though the registry would otherwise own its disposal.
///
/// This drives "Window 1": a registry pre-filled to capacity so <c>Register</c> throws. The
/// returned sub instance implements <see cref="IDisposable"/>; once the fix wraps the registration
/// in a try/catch that disposes the orphaned instance on failure, this test goes green.
/// RED today: the orphaned instance is never disposed.
/// </summary>
public sealed class Round1_SubServiceDispatcherLeakTests
{
    // ISubService extends IDisposable so a single DispatchProxy can satisfy both the generated
    // sub-service interface shape AND IDisposable, letting the dispatcher's disposal path observe it.
    private const string Source = """
        using ShaRPC.Core.Attributes;
        using System;
        using System.Threading.Tasks;

        namespace Round1.Leak
        {
            [ShaRpcService]
            public interface ISubService : IDisposable
            {
                Task<int> CountAsync();
            }

            [ShaRpcService]
            public interface IRootService
            {
                Task<ISubService> GetSubAsync(string label);
            }
        }
        """;

    [Fact]
    public async Task Dispatcher_DisposesReturnedSubService_WhenRegistryRegisterThrows()
    {
        var asm = Compile(Source);

        var dispatcherType = asm.GetType("Round1.Leak.RootServiceDispatcher")!;
        var iRoot = asm.GetType("Round1.Leak.IRootService")!;
        var iSub = asm.GetType("Round1.Leak.ISubService")!;

        // The sub instance the root impl will return; it records its own disposal.
        var disposalSink = new DisposalSink();
        var subImpl = SubImplFactory.Create(iSub, disposalSink);
        var rootImpl = RootImplFactory.Create(iRoot, _ => subImpl);

        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType, rootImpl)!;

        // Registry at capacity: one slot, already filled. Register must throw for the sub instance.
        var registry = new InstanceRegistry(maxInstances: 1);
        registry.Register("Filler", new object());

        var serializer = new TestJsonSerializer();
        using var labelPayload = serializer.SerializeToPayload<string>("hello");

        // The registry-full failure must surface to the caller...
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.DispatchToPayloadAsync(
                "GetSubAsync",
                labelPayload.Memory,
                serializer,
                registry,
                CancellationToken.None));

        // ...and the orphaned sub-service instance must have been disposed by the dispatcher.
        // RED today: there is no try/catch, so __sub leaks and Disposed stays false.
        Assert.True(
            disposalSink.Disposed,
            "the sub-service instance returned by the impl must be disposed when registry.Register fails, "
            + "otherwise the instance leaks (IDisposable/IAsyncDisposable never invoked).");
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
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString())));
        return Assembly.Load(ms.ToArray());
    }

    /// <summary>Records whether Dispose was observed on the proxied sub instance.</summary>
    public sealed class DisposalSink
    {
        public bool Disposed;
    }

    /// <summary>Builds an in-memory <c>ISubService</c> (which also implements IDisposable).</summary>
    private static class SubImplFactory
    {
        public static object Create(Type subIface, DisposalSink sink)
        {
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(subIface, typeof(SubStub));
            var proxy = closed.Invoke(null, null)!;
            ((SubStub)proxy).Sink = sink;
            return proxy;
        }
    }

    public class SubStub : DispatchProxy
    {
        public DisposalSink? Sink;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "Dispose")
            {
                if (Sink is not null) Sink.Disposed = true;
                return null;
            }

            if (targetMethod?.Name == "CountAsync") return Task.FromResult(0);
            throw new InvalidOperationException("unexpected " + targetMethod?.Name);
        }
    }

    /// <summary>Builds an in-memory <c>IRootService</c> that hands out the supplied sub-impl.</summary>
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
                    .Invoke(null, new[] { sub });
            }

            throw new InvalidOperationException("unexpected " + targetMethod?.Name);
        }
    }
}
