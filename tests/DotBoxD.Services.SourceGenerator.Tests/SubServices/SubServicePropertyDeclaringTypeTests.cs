using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public sealed class SubServicePropertyDeclaringTypeTests
{
    [Fact]
    public void InheritedSubServicePropertyFromGenericBase_UsesClosedDeclaringInterfaceCast()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.GenericBaseProperty
            {
                [DotBoxDService]
                public interface ISub
                {
                    int Count();
                }

                public interface IBase<T>
                {
                    T Child { get; }
                }

                [DotBoxDService]
                public interface IRoot : IBase<ISub>
                {
                }
            }
            """;

        var (final, runResult) = Run(source);

        AssertCompiles(final);
        Extensions(runResult).Should().Contain(
            "((global::Regress.GenericBaseProperty.IBase<global::Regress.GenericBaseProperty.ISub>)implementation).Child");
    }

    [Fact]
    public void InheritedSubServicePropertyFromNestedBase_UsesNestedDeclaringInterfaceCast()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.NestedBaseProperty
            {
                [DotBoxDService]
                public interface ISub
                {
                    int Count();
                }

                public static class Host
                {
                    public interface IBase
                    {
                        ISub Child { get; }
                    }
                }

                [DotBoxDService]
                public interface IRoot : Host.IBase
                {
                }
            }
            """;

        var (final, runResult) = Run(source);

        AssertCompiles(final);
        Extensions(runResult).Should().Contain(
            "((global::Regress.NestedBaseProperty.Host.IBase)implementation).Child");
    }

    private static string Extensions(GeneratorDriverRunResult runResult) =>
        runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "DotBoxDRpcExtensions.g.cs")
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
}
