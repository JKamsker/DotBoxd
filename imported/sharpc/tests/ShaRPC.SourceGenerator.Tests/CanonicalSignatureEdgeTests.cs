using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class CanonicalSignatureEdgeTests
{
    [Fact]
    public void InheritedDynamicAndObjectMethods_DeduplicateBySignature()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.CanonicalDynamicSignatures
            {
                public interface ILeft
                {
                    void M(dynamic value);
                }

                public interface IRight
                {
                    void M(object value);
                }

                [ShaRpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC003");
    }

    [Fact]
    public void InheritedRefOutMethods_RejectServiceInsteadOfEmittingDuplicateStubs()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.CanonicalRefOutSignatures
            {
                public interface ILeft
                {
                    void M(ref int value);
                }

                public interface IRight
                {
                    void M(out int value);
                }

                [ShaRpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("incompatible parameter ref kinds"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("ICombined."));
    }

    [Fact]
    public void InheritedNestedConstructedTypes_DoNotCollapseDistinctOverloads()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.CanonicalNestedConstructedSignatures
            {
                public sealed class Outer<T>
                {
                    public sealed class Inner
                    {
                    }
                }

                public interface ILeft
                {
                    void Take(Outer<int>.Inner value);
                }

                public interface IRight
                {
                    void Take(Outer<string>.Inner value);
                }

                [ShaRpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Where(d => d.Id == "SHARPC002").Should().HaveCount(2);
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("ICombined.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        CountOccurrences(proxy, "Take(").Should().Be(2);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
