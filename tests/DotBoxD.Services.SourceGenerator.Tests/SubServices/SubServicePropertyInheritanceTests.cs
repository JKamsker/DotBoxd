using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class SubServicePropertyInheritanceTests
{
    [Fact]
    public void DiamondInheritedSubServiceProperties_DeduplicateGeneratedProxyMembers()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServicePropertyInheritance
            {
                [DotBoxDService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                public interface ILeft
                {
                    ISub Child { get; }
                }

                public interface IRight
                {
                    ISub Child { get; }
                }

                [DotBoxDService]
                public interface IRoot : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        AssertCompiles(final);

        var proxy = GetRootProxy(runResult);
        CountOccurrences(proxy, "private readonly global::Regress.SubServicePropertyInheritance.ISub __dotboxd_Child;")
            .Should().Be(1);
        CountOccurrences(proxy, "public global::Regress.SubServicePropertyInheritance.ISub Child =>")
            .Should().Be(1);
    }

    [Fact]
    public void HiddenInheritedSubServiceProperty_DeduplicatesCompatibleProperty()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServicePropertyInheritance
            {
                [DotBoxDService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                public interface IBase
                {
                    ISub Child { get; }
                }

                [DotBoxDService]
                public interface IRoot : IBase
                {
                    new ISub Child { get; }
                }
            }
            """;

        var (final, runResult) = Run(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        AssertCompiles(final);

        var proxy = GetRootProxy(runResult);
        CountOccurrences(proxy, "private readonly global::Regress.SubServicePropertyInheritance.ISub __dotboxd_Child;")
            .Should().Be(1);
        CountOccurrences(proxy, "public global::Regress.SubServicePropertyInheritance.ISub Child =>")
            .Should().Be(1);
    }

    [Fact]
    public void InheritedSubServicePropertiesWithDifferentNullableAnnotations_RejectService()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServicePropertyInheritance
            {
                [DotBoxDService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                public interface ILeft
                {
                    ISub Child { get; }
                }

                public interface IRight
                {
                    ISub? Child { get; }
                }

                [DotBoxDService]
                public interface IRoot : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("incompatible return type"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IRoot."));
    }

    private static string GetRootProxy(GeneratorDriverRunResult runResult) =>
        runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();

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

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
