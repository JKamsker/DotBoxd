using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxd.Services.SourceGenerator.Tests;

public class LiteralEscapingTests
{
    [Fact]
    public void CustomWireNames_WithLowControlCharacters_EscapeEveryGeneratedLiteral()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.LowControlWireNames
            {
                [DotBoxdService(Name = "svc\a\b\f\v\u001fend")]
                public interface IControlNames
                {
                    [DotBoxdMethod(Name = "method\a\b\f\v\u001fend")]
                    Task<int> GetAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        const string serviceLiteral = "\"svc\\a\\b\\f\\v\\u001fend\"";
        const string methodLiteral = "\"method\\a\\b\\f\\v\\u001fend\"";
        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IControlNames.DotBoxdRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain(serviceLiteral);
        proxy.Should().Contain(methodLiteral);

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IControlNames.DotBoxdRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().Contain(serviceLiteral);
        dispatcher.Should().Contain("case " + methodLiteral + ":");
    }

    private static (CSharpCompilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (compilation.AddSyntaxTrees(runResult.GeneratedTrees), runResult);
    }

    private static void AssertCompiles(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }
}
