using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

/// <summary>
/// A DotBoxD service property publicly advertises a get-only sub-service control surface, but an
/// indexer cannot be re-expressed by the current proxy generator, which only emits ordinary named
/// properties. The generator should fail closed with DBXS003 instead of accepting the shape and
/// leaking raw compiler errors from broken generated source.
/// </summary>
public sealed class Round9_ServiceIndexerPropertyDiagnosticTests
{
    [Fact]
    public void ServiceIndexerProperty_ProducesDBXS003_AndSkipsTheService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.IndexerProperty
            {
                [DotBoxDService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [DotBoxDService]
                public interface IRoot
                {
                    ISub this[int slot] { get; }
                    Task<int> PingAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        var compilerErrors = emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();

        using (new AssertionScope())
        {
            runResult.Diagnostics.Should().Contain(
                d => d.Id == "DBXS003" && d.GetMessage().Contains("indexer"),
                "a sub-service indexer is not a shape the generator can reproduce honestly");

            runResult.Results.Single().GeneratedSources.Should().NotContain(
                g => g.HintName.Contains("IRoot"),
                "unsupported service-member shapes should skip the affected service instead of generating broken code");

            emit.Success.Should().BeTrue(
                "unsupported indexer properties should fail closed in the generator instead of leaking raw compiler errors:{0}{1}",
                Environment.NewLine,
                string.Join(Environment.NewLine, compilerErrors));
        }
    }
}
