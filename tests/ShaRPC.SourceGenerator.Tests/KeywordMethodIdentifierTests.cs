using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class KeywordMethodIdentifierTests
{
    [Fact]
    public void VerbatimKeywordMethodName_EscapesCSharpCalls_ButKeepsWireNameUnescaped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.KeywordMethods
            {
                [ShaRpcService]
                public interface IKeywordMethods
                {
                    Task<int> @return(int @params);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IKeywordMethods.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("@return(int @params)");
        proxy.Should().Contain("\"return\", @params");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IKeywordMethods.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().Contain("case \"return\":");
        dispatcher.Should().Contain(".@return(arg)");

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }
}
