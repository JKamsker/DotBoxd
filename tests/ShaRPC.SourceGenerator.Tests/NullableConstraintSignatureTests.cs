using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class NullableConstraintSignatureTests
{
    [Fact]
    public void InheritedGenericMethods_WithEquivalentNullableConstraints_Deduplicate()
    {
        const string source = """
            #nullable enable
            using ShaRPC.Core.Attributes;

            namespace Regress.CanonicalNullableConstraintSignatures
            {
                public interface IFace
                {
                }

                public interface ILeft
                {
                    void Echo<T>(T value) where T : IFace?;
                }

                public interface IRight
                {
                    void Echo<U>(U value) where U : IFace?;
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
        runResult.Diagnostics.Where(d => d.Id == "SHARPC002").Should().ContainSingle();
    }

    [Fact]
    public void InheritedGenericMethods_WithDifferentNullableConstraints_RejectService()
    {
        const string source = """
            #nullable enable
            using ShaRPC.Core.Attributes;

            namespace Regress.CanonicalDifferentNullableConstraintSignatures
            {
                public interface IFace
                {
                }

                public interface ILeft
                {
                    void Echo<T>(T value) where T : IFace;
                }

                public interface IRight
                {
                    void Echo<U>(U value) where U : IFace?;
                }

                [ShaRpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("incompatible generic constraints"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("ICombined."));
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
