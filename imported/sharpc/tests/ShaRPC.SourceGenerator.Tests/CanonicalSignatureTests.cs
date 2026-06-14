using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class CanonicalSignatureTests
{
    [Fact]
    public void InheritedGenericMethods_WithDifferentTypeParameterNames_DeduplicateBySignature()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.CanonicalSignatures
            {
                public interface ILeft
                {
                    void Echo<T>(T value);
                }

                public interface IRight
                {
                    void Echo<U>(U value);
                }

                [ShaRpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Where(d => d.Id == "SHARPC002").Should().ContainSingle();
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("ICombined.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        CountOccurrences(proxy, "Echo<T>").Should().Be(1);
    }

    [Fact]
    public void InheritedGenericMethods_WithDifferentConstraints_RejectService()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.CanonicalConstraintSignatures
            {
                public interface ILeft
                {
                    void Echo<T>(T value) where T : class;
                }

                public interface IRight
                {
                    void Echo<U>(U value) where U : struct;
                }

                [ShaRpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("incompatible generic constraints"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("ICombined."));
    }

    [Fact]
    public void InheritedGenericMethods_WithEquivalentRecursiveConstraints_Deduplicate()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.CanonicalRecursiveConstraintSignatures
            {
                public interface IFace<T>
                {
                }

                public interface ILeft
                {
                    void Echo<T>(T value) where T : IFace<T>;
                }

                public interface IRight
                {
                    void Echo<U>(U value) where U : IFace<U>;
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
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("ICombined.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        CountOccurrences(proxy, "Echo<T>").Should().Be(1);
    }

    [Fact]
    public void InheritedGenericMethods_WithEquivalentConstraintsInDifferentOrder_Deduplicate()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.CanonicalOrderedConstraintSignatures
            {
                public interface IA
                {
                }

                public interface IB
                {
                }

                public interface ILeft
                {
                    void Echo<T>(T value) where T : IA, IB;
                }

                public interface IRight
                {
                    void Echo<U>(U value) where U : IB, IA;
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
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("ICombined.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        CountOccurrences(proxy, "Echo<T>").Should().Be(1);
    }

    [Fact]
    public void AsyncSiblingProjection_IgnoresTupleElementNamesForCollisionKeys()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Regress.CanonicalTupleSignatures
            {
                public interface ILeft
                {
                    int Echo((int A, int B) value);
                }

                public interface IRight
                {
                    Task<int> EchoAsync((int X, int Y) value, CancellationToken ct = default);
                }

                [ShaRpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC004" &&
            d.GetMessage().Contains("EchoAsync"));
        var asyncInterface = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("ICombined.ShaRpcAsync.g.cs"))
            .SourceText.ToString();
        CountOccurrences(asyncInterface, "EchoAsync").Should().Be(1);
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
