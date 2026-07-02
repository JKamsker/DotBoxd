using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public partial class CodegenRegressionTests
{
    [Fact]
    public void ZeroParamVoid_AtRuntime_SelectsNoResponseOverload()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.ZeroVoidRuntime
            {
                [RpcService]
                public interface IZvr
                {
                    void Ping();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        finalCompilation.Emit(ms).Success.Should().BeTrue();
        ms.Position = 0;
        var alc = new System.Runtime.Loader.AssemblyLoadContext(
            "ZeroVoidRuntime_" + Guid.NewGuid(), isCollectible: false);
        var asm = alc.LoadFromStream(ms);

        var client = new OverloadProbeClient();
        var proxyType = asm.GetType("Regress.ZeroVoidRuntime.ZvrProxy")!;
        var proxy = Activator.CreateInstance(proxyType, client)!;

        proxyType.GetMethod("Ping")!.Invoke(proxy, Array.Empty<object>());

        client.NoRequestNoResponseOverloadCalls.Should().Be(1,
            "the zero-parameter void path must use the parameterless Task InvokeAsync(service, method, ct) overload so no dummy request is serialized and the dispatcher receives no payload");
        client.WithRequestNoResponseOverloadCalls.Should().Be(0,
            "the void path must no longer route through the with-request overload that previously sent a throwaway new object()");
        client.WithResponseOverloadCalls.Should().Be(0,
            "Task<TResponse> InvokeAsync<TResponse>(...) is wrong for void — it would force the serializer to deserialize an empty response body");
    }

    /// <summary>A minimal IRpcInvoker that does nothing — for DBXS002 stub testing.</summary>
    private sealed class NullClient : global::DotBoxD.Services.Server.IRpcInvoker
    {
        public bool IsConnected => true;
        public Task ConnectAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<TR> InvokeAsync<TQ, TR>(string s, string m, TQ q, System.Threading.CancellationToken ct = default) => Task.FromResult(default(TR)!);
        public Task<TR> InvokeAsync<TR>(string s, string m, System.Threading.CancellationToken ct = default) => Task.FromResult(default(TR)!);
        public Task InvokeAsync<TQ>(string s, string m, TQ q, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task InvokeAsync(string s, string m, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;

        // Feature-2 instance overloads forward to the singleton ones so the existing
        // assertions still observe sub-routed calls if a test were to exercise them.
        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(string s, string id, string m, TQ q, System.Threading.CancellationToken ct = default)
            => InvokeAsync<TQ, TR>(s, m, q, ct);
        public Task<TR> InvokeOnInstanceAsync<TR>(string s, string id, string m, System.Threading.CancellationToken ct = default)
            => InvokeAsync<TR>(s, m, ct);
        public Task InvokeOnInstanceAsync<TQ>(string s, string id, string m, TQ q, System.Threading.CancellationToken ct = default)
            => InvokeAsync<TQ>(s, m, q, ct);
        public Task InvokeOnInstanceAsync(string s, string id, string m, System.Threading.CancellationToken ct = default)
            => InvokeAsync(s, m, ct);
    }

    /// <summary>
    /// Regression: a service interface declared inside the <c>DotBoxD.Services.*</c> namespace
    /// must still compile. The generated code references `global::DotBoxD.Services.IRpcInvoker`
    /// etc. — without the `global::` qualifier the user's namespace would shadow ours.
    /// </summary>
    [Fact]
    public void ServiceInDotBoxDRpcCoreNamespace_StillResolvesGlobalTypes()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace DotBoxD.Services.MyService
            {
                [RpcService]
                public interface IMine
                {
                    Task<int> CountAsync();
                }
            }
            """;

        var (final, _) = Run(source);
        AssertCompiles(final);
    }

    /// <summary>
    /// Regression: a service whose every method has an unsupported shape (ref/out)
    /// must still produce a proxy class that satisfies the interface — every method
    /// is a throwing stub — and a dispatcher whose switch has zero cases.
    /// </summary>
    [Fact]
    public void ServiceWithOnlyRefOutMethods_StillImplementsInterface_AndStubsAllThrow()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.AllUnsupported
            {
                [RpcService]
                public interface IAllBad
                {
                    void OnlyOut(out int x);
                    void OnlyRef(ref int x);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        finalCompilation.Emit(ms).Success.Should().BeTrue(
            "an all-unsupported service must still produce a class that implements the interface");

        runResult.Diagnostics.Where(d => d.Id == "DBXS002")
            .Should().HaveCount(2, "both methods should each surface DBXS002");

        ms.Position = 0;
        var alc = new System.Runtime.Loader.AssemblyLoadContext(
            "AllUnsupported_" + Guid.NewGuid(), isCollectible: false);
        var asm = alc.LoadFromStream(ms);
        var proxyType = asm.GetType("Regress.AllUnsupported.AllBadProxy")!;
        var proxy = Activator.CreateInstance(proxyType, new NullClient())!;

        var outArgs = new object[] { 0 };
        Assert.Throws<System.Reflection.TargetInvocationException>(
                () => proxyType.GetMethod("OnlyOut")!.Invoke(proxy, outArgs))
            .InnerException.Should().BeOfType<NotSupportedException>();

        var refArgs = new object[] { 0 };
        Assert.Throws<System.Reflection.TargetInvocationException>(
                () => proxyType.GetMethod("OnlyRef")!.Invoke(proxy, refArgs))
            .InnerException.Should().BeOfType<NotSupportedException>();
    }

    /// <summary>
    /// Regression: <see cref="DotBoxD.Services.Attributes.RpcMethodAttribute"/> declared on
    /// a BASE interface method must propagate to the wire name used by the DERIVED proxy.
    /// </summary>
    [Fact]
    public void InheritedDotBoxDMethodNameAttribute_IsUsedAsWireMethodName()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.InheritWire
            {
                public interface IBaseWire
                {
                    [RpcMethod(Name = "wire_name")]
                    Task<int> FetchAsync(int id);
                }

                [RpcService]
                public interface IDerivedWire : IBaseWire
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.InheritWire", "IDerivedWire", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().Contain("case \"wire_name\":",
            "the inherited [RpcMethod(Name=...)] must drive the dispatcher's case literal");
        dispatcher.Should().NotContain("case \"FetchAsync\":",
            "the CLR name must not leak into the wire when an explicit name is set");
    }

    /// <summary>
    /// RPC dispatch is keyed only by wire method name. Overloads that keep the default
    /// CLR name would produce duplicate switch cases and route incorrectly, so every
    /// colliding method is diagnosed and emitted as a proxy stub.
    /// </summary>
    [Fact]
    public void OverloadedServiceMethods_WithDefaultWireNames_AreDiagnosedAndOmittedFromDispatcher()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.OverloadDefault
            {
                [RpcService]
                public interface ILookup
                {
                    Task<int> GetAsync(int id);
                    Task<string> GetAsync(string name);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Where(d => d.Id == "DBXS002")
            .Should().HaveCount(2, "both overloads share the same wire name and cannot be routed safely");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.OverloadDefault", "ILookup", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetAsync\":");
    }

    /// <summary>
    /// Overloaded CLR method names are supported when the user gives each overload a
    /// distinct wire name through <see cref="DotBoxD.Services.Attributes.RpcMethodAttribute"/>.
    /// </summary>
    [Fact]
    public void OverloadedServiceMethods_WithDistinctWireNames_GenerateDistinctDispatcherCases()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.OverloadNamed
            {
                [RpcService]
                public interface ILookup
                {
                    [RpcMethod(Name = "GetById")]
                    Task<int> GetAsync(int id);

                    [RpcMethod(Name = "GetByName")]
                    Task<string> GetAsync(string name);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.OverloadNamed", "ILookup", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().Contain("case \"GetById\":");
        dispatcher.Should().Contain("case \"GetByName\":");
    }

}
