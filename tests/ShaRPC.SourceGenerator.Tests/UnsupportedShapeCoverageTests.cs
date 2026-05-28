using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class UnsupportedShapeCoverageTests
{
    [Fact]
    public void RefLikeReturnType_ProducesSHARPC002_AndCompilingProxyStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System;

            namespace Regress.UnsupportedCoverage
            {
                [ShaRpcService]
                public interface IRefLikeReturn
                {
                    ReadOnlySpan<byte> GetBytes();
                }
            }
            """;

        var (_, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("return type uses a ref-like type"));
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRefLikeReturn.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("global::System.ReadOnlySpan<byte> GetBytes()");
        proxy.Should().Contain("throw new global::System.NotSupportedException");
    }

    [Fact]
    public void MultipleCancellationTokenParameters_ProducesSHARPC002_AtSecondToken()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedCoverage
            {
                [ShaRpcService]
                public interface IMultipleTokens
                {
                    Task<int> BadAsync(int x, CancellationToken first = default, CancellationToken second = default);
                    Task<int> GoodAsync(int x, CancellationToken ct = default);
                }
            }
            """;

        var (_, runResult) = Compile(source);

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "SHARPC002");
        diagnostic.GetMessage().Should().Contain("multiple CancellationToken parameters are not supported");
        DiagnosticText(source, diagnostic).Should().StartWith("second");

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IMultipleTokens.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IMultipleTokens.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"BadAsync\":");
        dispatcher.Should().Contain("case \"GoodAsync\":");
    }

    [Fact]
    public void InParameter_ProducesSHARPC002_AndPreservesProxyStubSignature()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.UnsupportedCoverage
            {
                [ShaRpcService]
                public interface IInParameter
                {
                    void Inspect(in int value);
                }
            }
            """;

        var (_, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("parameter 'value' uses an unsupported pass-by-reference kind 'in'"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IInParameter.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("public void Inspect(in int value)");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IInParameter.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"Inspect\":");
    }

    private static (Compilation Compilation, GeneratorDriverRunResult RunResult) Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return (finalCompilation, runResult);
    }

    private static string DiagnosticText(string source, Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var line = source.Replace("\r\n", "\n").Split('\n')[span.StartLinePosition.Line];
        return line.Substring(span.StartLinePosition.Character);
    }
}
