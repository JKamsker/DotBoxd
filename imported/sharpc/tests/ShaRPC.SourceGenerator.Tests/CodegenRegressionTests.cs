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
    public void InheritedSameSignatureMethods_WithDifferentReturns_ProduceSHARPC003()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.InheritConflict
            {
                public interface IA
                {
                    int M();
                }

                public interface IB
                {
                    string M();
                }

                [ShaRpcService]
                public interface IC : IA, IB
                {
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("same signature as another method but an incompatible return type"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IC."));
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
    public void RefReturnMethod_ProducesSHARPC002_AndCompilingProxyStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.RefReturn
            {
                [ShaRpcService]
                public interface IRefReturn
                {
                    ref int GetRef();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("return value uses an unsupported pass-by-reference kind 'ref'"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.RefReturn", "IRefReturn", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("public ref int GetRef()");

        var dispatcher = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.RefReturn", "IRefReturn", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetRef\":");
    }

    [Fact]
    public void RefLikeParameterType_ProducesSHARPC002_AndCompilingProxyStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System;

            namespace Regress.RefLikePayload
            {
                [ShaRpcService]
                public interface ISpanSvc
                {
                    int Count(ReadOnlySpan<byte> bytes);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("parameter 'bytes' uses a ref-like type"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.RefLikePayload", "ISpanSvc", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("Count(global::System.ReadOnlySpan<byte> bytes)");
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.RefLikePayload", "ISpanSvc", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"Count\":");
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
    public void ExistingAsyncSiblingType_ProducesSHARPC003_AndServiceIsSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                public interface IFooAsync
                {
                }

                [ShaRpcService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("generated async sibling interface 'IFooAsync' would collide"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void NonPublicServiceInterface_ProducesSHARPC003_AndIsSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.InternalService
            {
                [ShaRpcService]
                internal interface IInternal
                {
                    Task<int> CountAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("service interfaces must be public"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IInternal"));
    }

    [Fact]
    public void ServiceInterfaceWithProperty_ProducesSHARPC003_AndIsSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.PropertyMember
            {
                [ShaRpcService]
                public interface IWithProperty
                {
                    int Count { get; }
                    Task<int> CountAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("interface property 'Count' is not supported"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IWithProperty"));
    }

    [Fact]
    public void ServiceInterfaceWithEvent_ProducesSHARPC003_AndIsSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace Regress.EventMember
            {
                [ShaRpcService]
                public interface IWithEvent
                {
                    event EventHandler Changed;
                    Task<int> CountAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("interface event 'Changed' is not supported"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IWithEvent"));
    }

    [Fact]
    public void ServiceInterfaceWithStaticAbstractMethod_ProducesSHARPC003_AndIsSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.StaticMember
            {
                [ShaRpcService]
                public interface IWithStatic
                {
                    static abstract Task<int> CountAsync();
                    Task<int> InstanceCountAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("static interface method 'CountAsync' is not supported"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IWithStatic"));
    }

    [Fact]
    public void ServiceInterfaceWithPrivateMethod_ProducesSHARPC003_AndIsSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.PrivateMethod
            {
                [ShaRpcService]
                public interface IWithPrivate
                {
                    private int Hidden() => 1;
                    int Visible();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("non-public interface method 'Hidden' is not supported"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IWithPrivate"));
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
    public void EscapedKeywordServiceInterfaceName_CompilesGeneratedOutput()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.KeywordType
            {
                [ShaRpcService]
                public interface @event
                {
                    Task<int> CountAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.KeywordType", "event", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("global::Regress.KeywordType.@event");

        var extensions = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "ShaRpcExtensions.g.cs")
            .SourceText.ToString();
        extensions.Should().Contain("global::Regress.KeywordType.@event");
    }

    [Fact]
    public void EscapedKeywordNamespaceAndServiceName_CompileGeneratedOutput()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.@event
            {
                [ShaRpcService]
                public interface @class
                {
                    Task<int> CountAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxyHint = GeneratorTestHelper.HintName(
            "Regress.@event", "class", GeneratorTestHelper.GeneratedKind.Proxy);
        proxyHint.Should().NotContain("@");

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == proxyHint)
            .SourceText.ToString();
        proxy.Should().Contain("namespace Regress.@event");
        proxy.Should().Contain("global::Regress.@event.@class");
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

    [Fact]
    public void ServiceAndSubServiceNames_WithQuotesAndBraces_AreEscapedEverywhere()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServiceEscape
            {
                [ShaRpcService(Name = "Sub\"{Svc")]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService(Name = "Root{Svc")]
                public interface IRoot
                {
                    Task<ISub> GetSubAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.SubServiceEscape", "IRoot", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().Contain("registry.Register(\"Sub\\\"{Svc\", __sub)");
        dispatcher.Should().Contain("ServiceName = \"Sub\\\"{Svc\"");
        dispatcher.Should().Contain("\"Method '\" + method + \"' not found on service 'Root{Svc'.\"");
    }

    [Fact]
    public void GlobalNamespaceSubServiceReturn_UsesGeneratedSubProxyTypeName()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            [ShaRpcService]
            public interface ISub
            {
                Task<int> CountAsync();
            }

            [ShaRpcService]
            public interface IRoot
            {
                Task<ISub> GetSubAsync();
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "IRoot.ShaRpcProxy.g.cs")
            .SourceText.ToString();
        proxy.Should().Contain("return new global::SubProxy(this._invoker, __sharpc_handle.InstanceId);");
        proxy.Should().NotContain("global::ISubProxy");
    }

    [Fact]
    public void SynchronousSubServiceReturn_ProducesSHARPC002_AndDoesNotSerializeLiveInstance()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SyncSubService
            {
                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    ISub GetSub();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("synchronous sub-service returns are not supported"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.SyncSubService", "IRoot", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.SyncSubService", "IRoot", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetSub\":");
        dispatcher.Should().NotContain("SerializeToPayload");
    }

    [Fact]
    public void GenericSubServiceReturn_ProducesSHARPC002_AndDoesNotBuildInvalidProxyType()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GenericSubService
            {
                [ShaRpcService]
                public interface IChild<T>
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<IChild<int>> GetChildAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003",
            "the generic sub-service interface itself is rejected");
        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("generic sub-service return types are not supported"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.GenericSubService", "IRoot", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().NotContain("Child<int>Proxy");
    }

    [Fact]
    public void NestedSubServiceReturn_ProducesSHARPC002_AndDoesNotBuildInvalidProxyType()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NestedSubService
            {
                public class Outer
                {
                    [ShaRpcService]
                    public interface IInner
                    {
                        Task<int> CountAsync();
                    }
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<Outer.IInner> GetInnerAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003",
            "the nested sub-service interface itself is rejected");
        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("nested sub-service return types are not supported"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.NestedSubService", "IRoot", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().NotContain("InnerProxy");
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
                [ShaRpcService(Name = "HintA.IFoo")]
                public interface IFoo
                {
                    Task<int> AAsync();
                }
            }
            namespace Regress.HintB
            {
                [ShaRpcService(Name = "HintB.IFoo")]
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
    /// Regression: namespaces that differ only by dot-vs-underscore flattening must not
    /// collide in hint names or generated extension methods.
    /// </summary>
    [Fact]
    public void DotAndUnderscoreNamespaceShapes_DoNotCollideOnHintNamesOrExtensions()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Flat
            {
                [ShaRpcService(Name = "Dotted.IFoo")]
                public interface IFoo
                {
                    Task<int> FromDottedAsync();
                }
            }

            namespace Regress_Flat
            {
                [ShaRpcService(Name = "Underscore.IFoo")]
                public interface IFoo
                {
                    Task<int> FromUnderscoreAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var hints = runResult.Results.Single().GeneratedSources
            .Select(g => g.HintName)
            .OrderBy(h => h)
            .ToArray();

        var proxyHints = hints.Where(h => h.EndsWith("IFoo.ShaRpcProxy.g.cs", StringComparison.Ordinal)).ToArray();
        proxyHints.Should().HaveCount(2);
        proxyHints.Should().OnlyHaveUniqueItems();
        proxyHints.Should().Contain("Regress_Flat_IFoo.ShaRpcProxy.g.cs");
        proxyHints.Should().Contain(h => h.StartsWith("Regress_Flat__", StringComparison.Ordinal),
            "the underscore namespace should get a deterministic disambiguator");

        var extensions = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "ShaRpcExtensions.g.cs")
            .SourceText.ToString();
        extensions.Should().Contain("GetRegress_Flat_Foo");
        extensions.Should().Contain("GetRegress_Flat__");
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
        // The proxy now exposes two constructors (top-level and instance-scoped); pick
        // the single-arg one to construct a top-level proxy for the test.
        var topLevelCtor = proxyType.GetConstructors().Single(c => c.GetParameters().Length == 1);
        var client = Activator.CreateInstance(typeof(NullClient))!;
        var proxy = topLevelCtor.Invoke(new[] { client })!;

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
    /// Behavioral test: confirms at runtime that the zero-parameter void path selects the
    /// parameterless <c>Task InvokeAsync(service, method, ct)</c> overload — sending no request
    /// and expecting no response — rather than the with-request overload (which used to send a
    /// throwaway <c>new object()</c>) or the no-request <c>Task&lt;TResponse&gt;</c> overload
    /// (which would corrupt server-side serialization).
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
            "the zero-parameter void path must use the parameterless Task InvokeAsync(service, method, ct) overload so no dummy request is serialized and the dispatcher receives no payload");
        client.WithRequestNoResponseOverloadCalls.Should().Be(0,
            "the void path must no longer route through the with-request overload that previously sent a throwaway new object()");
        client.WithResponseOverloadCalls.Should().Be(0,
            "Task<TResponse> InvokeAsync<TResponse>(...) is wrong for void — it would force the serializer to deserialize an empty response body");
    }

    /// <summary>A minimal IRpcInvoker that does nothing — for SHARPC002 stub testing.</summary>
    private sealed class NullClient : global::ShaRPC.Core.IRpcInvoker
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
    /// Regression: a service interface declared inside the <c>ShaRPC.Core.*</c> namespace
    /// must still compile. The generated code references `global::ShaRPC.Core.IRpcInvoker`
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
    /// RPC dispatch is keyed only by wire method name. Overloads that keep the default
    /// CLR name would produce duplicate switch cases and route incorrectly, so every
    /// colliding method is diagnosed and emitted as a proxy stub.
    /// </summary>
    [Fact]
    public void OverloadedServiceMethods_WithDefaultWireNames_AreDiagnosedAndOmittedFromDispatcher()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.OverloadDefault
            {
                [ShaRpcService]
                public interface ILookup
                {
                    Task<int> GetAsync(int id);
                    Task<string> GetAsync(string name);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Where(d => d.Id == "SHARPC002")
            .Should().HaveCount(2, "both overloads share the same wire name and cannot be routed safely");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.OverloadDefault", "ILookup", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetAsync\":");
    }

    /// <summary>
    /// Overloaded CLR method names are supported when the user gives each overload a
    /// distinct wire name through <see cref="ShaRPC.Core.Attributes.ShaRpcMethodAttribute"/>.
    /// </summary>
    [Fact]
    public void OverloadedServiceMethods_WithDistinctWireNames_GenerateDistinctDispatcherCases()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.OverloadNamed
            {
                [ShaRpcService]
                public interface ILookup
                {
                    [ShaRpcMethod(Name = "GetById")]
                    Task<int> GetAsync(int id);

                    [ShaRpcMethod(Name = "GetByName")]
                    Task<string> GetAsync(string name);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC002");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.OverloadNamed", "ILookup", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().Contain("case \"GetById\":");
        dispatcher.Should().Contain("case \"GetByName\":");
    }

    [Fact]
    public void DistinctMethods_WithSameCustomWireName_AreDiagnosedAndOmittedFromDispatcher()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.CustomWireCollision
            {
                [ShaRpcService]
                public interface ILookup
                {
                    [ShaRpcMethod(Name = "lookup")]
                    Task<int> GetByIdAsync(int id);

                    [ShaRpcMethod(Name = "lookup")]
                    Task<string> GetByNameAsync(string name);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Where(d => d.Id == "SHARPC002")
            .Should().HaveCount(2, "both methods share the same explicit wire name");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.CustomWireCollision", "ILookup", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"lookup\":");
    }

    /// <summary>
    /// Generic service methods are not routable by ShaRPC's current method-name based
    /// protocol. The proxy still has to implement the user interface, so it emits a
    /// generic throwing stub with matching constraints.
    /// </summary>
    [Fact]
    public void GenericServiceMethod_ProducesSHARPC002_AndCompilingProxyStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GenericMethod
            {
                [ShaRpcService]
                public interface IGenericMethod
                {
                    Task<T> EchoAsync<T>(T value) where T : class;
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("generic service methods are not supported"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.GenericMethod", "IGenericMethod", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("EchoAsync<T>(T value) where T : class");
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.GenericMethod", "IGenericMethod", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"EchoAsync\":");
    }

    [Fact]
    public void GenericServiceMethod_WithKeywordTypeParameter_EmitsCompilingProxyStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GenericKeyword
            {
                [ShaRpcService]
                public interface IGenericKeyword
                {
                    Task<@class> EchoAsync<@class>(@class value) where @class : class;
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("generic service methods are not supported"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.GenericKeyword", "IGenericKeyword", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("EchoAsync<@class>(@class value) where @class : class");
        proxy.Should().Contain("global::System.Threading.Tasks.Task<@class>");
    }

    /// <summary>
    /// Regression: <see cref="System.Threading.CancellationToken"/> parameters can be
    /// written through aliases and can appear before later payload parameters. The
    /// proxy must preserve the user's signature while excluding the token from the
    /// serialized request tuple, and the dispatcher must pass the runtime token back in
    /// the original argument position.
    /// </summary>
    [Fact]
    public void CancellationTokenAliasInMiddle_PreservesSignatureAndIsNotSerialized()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;
            using CT = System.Threading.CancellationToken;

            namespace Regress.CtOrder
            {
                [ShaRpcService]
                public interface ICtOrder
                {
                    Task<int> SumAsync(int a, CT cancellationToken, int b);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.CtOrder", "ICtOrder", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain(
            "SumAsync(int a, global::System.Threading.CancellationToken cancellationToken, int b)");
        proxy.Should().Contain(
            "InvokeAsync<(int, int), int>(\"ICtOrder\", \"SumAsync\", (a, b), cancellationToken)");

        var dispatcher = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.CtOrder", "ICtOrder", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().Contain("_service.SumAsync(args.Item1, ct, args.Item2)");
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
    private sealed class ValueReturningClient : global::ShaRPC.Core.IRpcInvoker
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
        public Task InvokeAsync(string s, string m, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;
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

    /// <summary>An IRpcInvoker that records which overload was actually called.</summary>
    private sealed class OverloadProbeClient : global::ShaRPC.Core.IRpcInvoker
    {
        public int WithRequestWithResponseOverloadCalls;
        public int WithResponseOverloadCalls;
        public int WithRequestNoResponseOverloadCalls;
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
            WithRequestNoResponseOverloadCalls++;
            return Task.CompletedTask;
        }
        public Task InvokeAsync(string s, string m, System.Threading.CancellationToken ct = default)
        {
            NoRequestNoResponseOverloadCalls++;
            return Task.CompletedTask;
        }
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
}
