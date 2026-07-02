using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

/// <summary>
/// Regression coverage for codegen issues caught during review:
/// inherited interface members, ValueTask support, ref/in/out rejection,
/// generic-interface rejection, nested-interface rejection, reserved-keyword
/// escaping, string-literal escaping, and global:: qualification.
/// Each test compiles the user source + generated source end-to-end via
/// <c>Emit</c>
/// so a generated file that doesn't actually compile would fail the test.
/// </summary>
public partial class CodegenRegressionTests
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
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Inherit
            {
                public interface IBase
                {
                    Task<int> FromBaseAsync(int x);
                }

                [RpcService]
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
    public void InheritedSameSignatureMethods_WithDifferentReturns_ProduceDBXS003()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

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

                [RpcService]
                public interface IC : IA, IB
                {
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("same signature as another method but an incompatible return type"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IC."));
    }

    [Fact]
    public void ValueTaskReturnTypes_AreSupported()
    {
        // Regression for H3: ValueTask/ValueTask<T> must not be classified as a sync return.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ValueTaskNs
            {
                [RpcService]
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
    public void RefAndOutParameters_ProduceDBXS002_AndOtherMethodsStillCompile()
    {
        // Regression for H2: ref/in/out parameters must be diagnosed via DBXS002 and
        // the offending method must be skipped — but the rest of the service still
        // generates valid code.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.RefOut
            {
                [RpcService]
                public interface IRefOut
                {
                    void BadOut(out int x);
                    void BadRef(ref int x);
                    Task<int> GoodAsync(int x);
                }
            }
            """;

        var (final, runResult) = Run(source);

        runResult.Diagnostics.Where(d => d.Id == "DBXS002")
            .Should().HaveCount(2, "BadOut and BadRef should each surface DBXS002");

        // The Good method should still flow through and compile.
        AssertCompiles(final);
    }

    [Fact]
    public void RefReturnMethod_ProducesDBXS002_AndCompilingProxyStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.RefReturn
            {
                [RpcService]
                public interface IRefReturn
                {
                    ref int GetRef();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
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
    public void RefLikeParameterType_ProducesDBXS002_AndCompilingProxyStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;

            namespace Regress.RefLikePayload
            {
                [RpcService]
                public interface ISpanSvc
                {
                    int Count(ReadOnlySpan<byte> bytes);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
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
    public void GenericServiceInterface_ProducesDBXS003_AndIsSkipped()
    {
        // Regression for C2: generic service interfaces are unsupported. The generator
        // must emit DBXS003 and NOT emit broken (non-generic) proxy code.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Generic
            {
                [RpcService]
                public interface IRepo<T>
                {
                    Task<T> GetAsync(string id);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003",
            "generic service interfaces must surface DBXS003");

        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IRepo"),
                "no proxy/dispatcher should be emitted for a rejected service");
    }

}
