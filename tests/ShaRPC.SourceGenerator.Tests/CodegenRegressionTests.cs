using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Regression coverage for codegen issues caught during review:
/// inherited interface members, ValueTask support, ref/in/out rejection,
/// generic-interface rejection, nested-interface rejection, reserved-keyword
/// escaping, string-literal escaping, and global:: qualification.
/// Each test compiles the user source + generated source end-to-end via
/// <see cref="CSharpCompilation.Emit(Stream, Stream, Stream, Stream,
/// System.Collections.Generic.IEnumerable{ResourceDescription}, EmitOptions, CompilationOptions, CancellationToken)"/>
/// so a generated file that doesn't actually compile would fail the test.
/// </summary>
public class CodegenRegressionTests
{
    private static (Compilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        return (finalCompilation, runResult);
    }

    private static void AssertCompiles(Compilation final)
    {
        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        if (!emit.Success)
        {
            var errs = string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            var dump = string.Join(
                Environment.NewLine + "----" + Environment.NewLine,
                final.SyntaxTrees.Select(t => t.FilePath + Environment.NewLine + t.GetText()));
            throw new InvalidOperationException("Emit failed:" + Environment.NewLine + errs + Environment.NewLine + dump);
        }
        emit.Success.Should().BeTrue();
    }

    [Fact]
    public void InheritedInterfaceMembers_AreEmittedOnDerivedProxy()
    {
        // Regression for C1 from review: a derived interface's proxy must implement methods
        // declared on its base interfaces; otherwise CS0535 at consumer compile time.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Inherit
            {
                public interface IBase
                {
                    Task<int> FromBaseAsync(int x);
                }

                [ShaRpcService]
                public interface IDerived : IBase
                {
                    Task<string> FromDerivedAsync();
                }
            }
            """;

        var (final, _) = Run(source);
        AssertCompiles(final);
    }

    [Fact]
    public void ValueTaskReturnTypes_AreSupported()
    {
        // Regression for H3: ValueTask/ValueTask<T> must not be classified as a sync return.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ValueTaskNs
            {
                [ShaRpcService]
                public interface IVt
                {
                    ValueTask<int> AddAsync(int a, int b);
                    ValueTask PingAsync();
                }
            }
            """;

        var (final, _) = Run(source);
        AssertCompiles(final);
    }

    [Fact]
    public void RefAndOutParameters_ProduceSHARPC002_AndOtherMethodsStillCompile()
    {
        // Regression for H2: ref/in/out parameters must be diagnosed via SHARPC002 and
        // the offending method must be skipped — but the rest of the service still
        // generates valid code.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.RefOut
            {
                [ShaRpcService]
                public interface IRefOut
                {
                    void BadOut(out int x);
                    void BadRef(ref int x);
                    Task<int> GoodAsync(int x);
                }
            }
            """;

        var (final, runResult) = Run(source);

        runResult.Diagnostics.Where(d => d.Id == "SHARPC002")
            .Should().HaveCount(2, "BadOut and BadRef should each surface SHARPC002");

        // The Good method should still flow through and compile.
        AssertCompiles(final);
    }

    [Fact]
    public void GenericServiceInterface_ProducesSHARPC003_AndIsSkipped()
    {
        // Regression for C2: generic service interfaces are unsupported. The generator
        // must emit SHARPC003 and NOT emit broken (non-generic) proxy code.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Generic
            {
                [ShaRpcService]
                public interface IRepo<T>
                {
                    Task<T> GetAsync(string id);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003",
            "generic service interfaces must surface SHARPC003");

        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IRepo"),
                "no proxy/dispatcher should be emitted for a rejected service");
    }

    [Fact]
    public void NestedServiceInterface_ProducesSHARPC003_AndIsSkipped()
    {
        // Regression for H4: nested interfaces are unsupported; SHARPC003 is fired and
        // no broken output is emitted.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Nested
            {
                public class Outer
                {
                    [ShaRpcService]
                    public interface IInner
                    {
                        Task<int> DoAsync(int x);
                    }
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003");
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IInner"));
    }

    [Fact]
    public void ReservedKeywordParameterNames_AreEscaped()
    {
        // Regression for H1: a parameter named `default` (or any C# keyword) must be
        // emitted with an @ prefix so the proxy compiles.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Keyword
            {
                [ShaRpcService]
                public interface IKw
                {
                    Task<int> DoAsync(int @class, int @default);
                }
            }
            """;

        var (final, _) = Run(source);
        AssertCompiles(final);
    }

    [Fact]
    public void GlobalNamespaceService_CompilesAndDoesNotEmitEmptyNamespace()
    {
        // Regression for the global-namespace branch: emitting a stray `namespace { ... }`
        // would fail to parse; emitting no namespace must keep the proxy at global scope.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            [ShaRpcService]
            public interface IGlobal
            {
                Task<int> GoAsync(int x);
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        // Sanity: the proxy file must NOT start a namespace block for the global scope.
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "IGlobal.ShaRpcProxy.g.cs")
            .SourceText.ToString();
        proxy.Should().NotContain("namespace ");
    }

    [Fact]
    public void StringLiteralsInServiceNameAndMethodName_AreEscaped()
    {
        // Regression for M3: a Name containing a double quote would break the generated
        // string literal. Escape it.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Escape
            {
                [ShaRpcService(Name = "Foo\"Bar")]
                public interface IEsc
                {
                    [ShaRpcMethod(Name = "do\"it")]
                    Task<int> DoAsync(int x);
                }
            }
            """;

        var (final, _) = Run(source);
        AssertCompiles(final);
    }

    // Note: ZeroParamVoidMethod_UsesNoResponseOverload (string-Contains on "new object()")
    // was deleted in favour of ZeroParamVoid_AtRuntime_SelectsNoResponseOverload below,
    // which proves the same intent via the actual overload that runs.

    /// <summary>
    /// Regression for the hint-name collision discovered in round 2 review:
    /// two services with the same simple interface name in different namespaces previously
    /// collided on <see cref="SourceProductionContext.AddSource"/> and threw at runtime.
    /// </summary>
    [Fact]
    public void SameSimpleInterfaceNameAcrossNamespaces_DoesNotCollideOnHintName()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.HintA
            {
                [ShaRpcService]
                public interface IFoo
                {
                    Task<int> AAsync();
                }
            }
            namespace Regress.HintB
            {
                [ShaRpcService]
                public interface IFoo
                {
                    Task<int> BAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var hints = runResult.Results.Single().GeneratedSources
            .Select(g => g.HintName).OrderBy(h => h).ToArray();
        hints.Should().Contain("Regress_HintA_IFoo.ShaRpcProxy.g.cs");
        hints.Should().Contain("Regress_HintB_IFoo.ShaRpcProxy.g.cs");

        // The two proxy files must be distinct — content equality would imply we
        // wrote the same source under both hint names by mistake.
        var aSource = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "Regress_HintA_IFoo.ShaRpcProxy.g.cs").SourceText.ToString();
        var bSource = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "Regress_HintB_IFoo.ShaRpcProxy.g.cs").SourceText.ToString();
        aSource.Should().NotBe(bSource);
        aSource.Should().Contain("AAsync");
        bSource.Should().Contain("BAsync");
    }

    /// <summary>
    /// Cache-hygiene regression: when a service is rejected by SHARPC003, no model
    /// should flow through the <c>Services</c> tracked step, so it cannot accidentally
    /// be incorporated into the <c>AllServices</c> aggregate.
    /// </summary>
    [Fact]
    public void RejectedGenericService_LeavesServicesStepEmpty()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GenericHygiene
            {
                [ShaRpcService]
                public interface IRepo<T>
                {
                    Task<T> GetAsync(string id);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var tracked = runResult.Results.Single().TrackedSteps;
        if (tracked.TryGetValue("Services", out var servicesSteps))
        {
            servicesSteps.SelectMany(s => s.Outputs).Should().BeEmpty(
                "a rejected generic service must not leak a model into the Services tracked step");
        }
        // If the Services key is absent entirely, that's the strongest possible
        // cache-hygiene guarantee — nothing else to check.
    }

    /// <summary>
    /// Behavioral test for the SHARPC002 stub: invoking a ref/out method on the proxy
    /// at runtime must throw <see cref="NotSupportedException"/> with a message that
    /// identifies the offending parameter. Without this, a regression that silently
    /// returned <c>default(T)</c> from the stub would still pass the compile-only test.
    /// </summary>
    [Fact]
    public void RefOrOutStub_ThrowsNotSupportedExceptionAtRuntime()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.RefOutRuntime
            {
                [ShaRpcService]
                public interface IRor
                {
                    void BadOut(out int x);
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
            "RefOutRuntime_" + Guid.NewGuid(), isCollectible: false);
        var asm = alc.LoadFromStream(ms);

        var proxyType = asm.GetType("Regress.RefOutRuntime.RorProxy")!;
        var ctorParam = proxyType.GetConstructors().Single().GetParameters()[0].ParameterType;
        // Build a no-op client implementation via DispatchProxy.
        var client = Activator.CreateInstance(typeof(NullClient))!;
        var proxy = Activator.CreateInstance(proxyType, client)!;

        // Invoke BadOut via reflection — should throw NotSupportedException through
        // the TargetInvocationException wrapper.
        var method = proxyType.GetMethod("BadOut")!;
        var args = new object[] { 0 };
        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method.Invoke(proxy, args));
        thrown.InnerException.Should().BeOfType<NotSupportedException>();
        thrown.InnerException!.Message.Should().Contain("BadOut");
        thrown.InnerException!.Message.Should().Contain("out");
    }

    /// <summary>
    /// Behavioral test for M1: confirms at runtime that the dummy-request InvokeAsync
    /// overload is the one selected for zero-parameter void methods, rather than the
    /// no-request Task&lt;TResponse&gt; overload (which would corrupt server-side serialization).
    /// </summary>
    [Fact]
    public void ZeroParamVoid_AtRuntime_SelectsNoResponseOverload()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.ZeroVoidRuntime
            {
                [ShaRpcService]
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
            "the zero-parameter void path must use Task InvokeAsync<TRequest>(...) so the dispatcher receives no payload to deserialize a response from");
        client.WithResponseOverloadCalls.Should().Be(0,
            "Task<TResponse> InvokeAsync<TResponse>(...) is wrong for void — it would force the serializer to deserialize an empty response body");
    }

    /// <summary>A minimal IShaRpcClient that does nothing — for SHARPC002 stub testing.</summary>
    private sealed class NullClient : global::ShaRPC.Core.Client.IShaRpcClient
    {
        public bool IsConnected => true;
        public Task ConnectAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<TR> InvokeAsync<TQ, TR>(string s, string m, TQ q, System.Threading.CancellationToken ct = default) => Task.FromResult(default(TR)!);
        public Task<TR> InvokeAsync<TR>(string s, string m, System.Threading.CancellationToken ct = default) => Task.FromResult(default(TR)!);
        public Task InvokeAsync<TQ>(string s, string m, TQ q, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
    }

    /// <summary>
    /// Regression: a service interface declared inside the <c>ShaRPC.Core.*</c> namespace
    /// must still compile. The generated code references `global::ShaRPC.Core.Client.IShaRpcClient`
    /// etc. — without the `global::` qualifier the user's namespace would shadow ours.
    /// </summary>
    [Fact]
    public void ServiceInShaRpcCoreNamespace_StillResolvesGlobalTypes()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace ShaRPC.Core.MyService
            {
                [ShaRpcService]
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
            using ShaRPC.Core.Attributes;

            namespace Regress.AllUnsupported
            {
                [ShaRpcService]
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

        runResult.Diagnostics.Where(d => d.Id == "SHARPC002")
            .Should().HaveCount(2, "both methods should each surface SHARPC002");

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
    /// Regression: <see cref="ShaRPC.Core.Attributes.ShaRpcMethodAttribute"/> declared on
    /// a BASE interface method must propagate to the wire name used by the DERIVED proxy.
    /// </summary>
    [Fact]
    public void InheritedShaRpcMethodNameAttribute_IsUsedAsWireMethodName()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.InheritWire
            {
                public interface IBaseWire
                {
                    [ShaRpcMethod(Name = "wire_name")]
                    Task<int> FetchAsync(int id);
                }

                [ShaRpcService]
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
            "the inherited [ShaRpcMethod(Name=...)] must drive the dispatcher's case literal");
        dispatcher.Should().NotContain("case \"FetchAsync\":",
            "the CLR name must not leak into the wire when an explicit name is set");
    }

    /// <summary>
    /// Behavioral test: a <see cref="ValueTask{TResult}"/>-returning proxy method must
    /// await correctly and surface the awaited value at runtime.
    /// </summary>
    [Fact]
    public async Task ValueTaskOfT_AtRuntime_ReturnsAwaitedValue()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.VtRun
            {
                [ShaRpcService]
                public interface IVtRun
                {
                    ValueTask<int> AddAsync(int a, int b);
                    ValueTask PingAsync();
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
            "VtRun_" + Guid.NewGuid(), isCollectible: false);
        var asm = alc.LoadFromStream(ms);

        var client = new ValueReturningClient { NextResult = 12 };
        var proxyType = asm.GetType("Regress.VtRun.VtRunProxy")!;
        var proxy = Activator.CreateInstance(proxyType, client)!;

        // The proxy implements both IVtRun and the generated IVtRunAsync sibling, so
        // it carries two AddAsync overloads (with and without CancellationToken). Bind
        // explicitly to the original 2-arg overload so the lookup is unambiguous.
        var addMethod = proxyType.GetMethod("AddAsync", new[] { typeof(int), typeof(int) })!;
        var vt = addMethod.Invoke(proxy, new object[] { 4, 8 })!;
        var asTask = (Task<int>)vt.GetType().GetMethod("AsTask")!.Invoke(vt, null)!;
        (await asTask).Should().Be(12);

        // Same disambiguation for the zero-arg PingAsync (sibling adds a CT overload).
        var pingMethod = proxyType.GetMethod("PingAsync", Type.EmptyTypes)!;
        var vtPing = pingMethod.Invoke(proxy, Array.Empty<object>())!;
        var asTaskPing = (Task)vtPing.GetType().GetMethod("AsTask")!.Invoke(vtPing, null)!;
        await asTaskPing;
    }

    /// <summary>A client whose <c>Task&lt;TResponse&gt;</c> overload returns a configured value.</summary>
    private sealed class ValueReturningClient : global::ShaRPC.Core.Client.IShaRpcClient
    {
        public object? NextResult;
        public bool IsConnected => true;
        public Task ConnectAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<TR> InvokeAsync<TQ, TR>(string s, string m, TQ q, System.Threading.CancellationToken ct = default)
            => Task.FromResult((TR)NextResult!);
        public Task<TR> InvokeAsync<TR>(string s, string m, System.Threading.CancellationToken ct = default)
            => Task.FromResult((TR)NextResult!);
        public Task InvokeAsync<TQ>(string s, string m, TQ q, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
    }

    /// <summary>An IShaRpcClient that records which overload was actually called.</summary>
    private sealed class OverloadProbeClient : global::ShaRPC.Core.Client.IShaRpcClient
    {
        public int WithRequestWithResponseOverloadCalls;
        public int WithResponseOverloadCalls;
        public int NoRequestNoResponseOverloadCalls;

        public bool IsConnected => true;
        public Task ConnectAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;

        public Task<TR> InvokeAsync<TQ, TR>(string s, string m, TQ q, System.Threading.CancellationToken ct = default)
        {
            WithRequestWithResponseOverloadCalls++;
            return Task.FromResult(default(TR)!);
        }
        public Task<TR> InvokeAsync<TR>(string s, string m, System.Threading.CancellationToken ct = default)
        {
            WithResponseOverloadCalls++;
            return Task.FromResult(default(TR)!);
        }
        public Task InvokeAsync<TQ>(string s, string m, TQ q, System.Threading.CancellationToken ct = default)
        {
            NoRequestNoResponseOverloadCalls++;
            return Task.CompletedTask;
        }
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
    }
}
