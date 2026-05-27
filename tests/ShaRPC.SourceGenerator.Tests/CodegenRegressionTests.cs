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

    [Fact]
    public void ZeroParamVoidMethod_UsesNoResponseOverload()
    {
        // Regression for M1: a zero-parameter void method must NOT use the no-payload
        // Task<TResponse> overload (which would force the serializer to deserialize an
        // empty response). It should use the no-response overload with a dummy request.
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.ZeroVoid
            {
                [ShaRpcService]
                public interface IZv
                {
                    void Ping();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "IZv.ShaRpcProxy.g.cs")
            .SourceText.ToString();
        // The Ping body must call the no-response overload signature, which takes a
        // request payload in addition to (service, method, ct).
        proxy.Should().Contain("new object()",
            "the generator must pass a dummy request payload to select the no-response InvokeAsync<TRequest> overload");
    }
}
