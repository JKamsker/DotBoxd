using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public class InheritedTupleElementNameTests
{
    [Fact]
    public void DuplicateInheritedMethodsWithSameTupleElementNames_Deduplicate()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedSameTupleNames
            {
                public interface ILeft
                {
                    int Echo((int A, int B) value);
                }

                public interface IRight
                {
                    int Echo((int A, int B) value);
                }

                [RpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        GetProxy(runResult).Should().Contain("public int Echo((int A, int B) value)");
    }

    [Fact]
    public void DuplicateInheritedMethodsWithDifferentTupleElementNames_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedTupleNames
            {
                public interface ILeft
                {
                    int Echo((int A, int B) value);
                }

                public interface IRight
                {
                    int Echo((int X, int Y) value);
                }

                [RpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        AssertRejectedForTupleNames(runResult);
    }

    [Fact]
    public void DuplicateInheritedMethodsWithNamedTupleAndValueTupleParameters_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedNamedTupleAndValueTuple
            {
                public interface ILeft
                {
                    int Echo((int A, int B) value);
                }

                public interface IRight
                {
                    int Echo(System.ValueTuple<int, int> value);
                }

                [RpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        AssertRejectedForTupleNames(runResult);
    }

    [Fact]
    public void DuplicateInheritedMethodsWithUnnamedTupleAndValueTupleParameters_Deduplicate()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedUnnamedTupleAndValueTuple
            {
                public interface ILeft
                {
                    int Echo((int, int) value);
                }

                public interface IRight
                {
                    int Echo(System.ValueTuple<int, int> value);
                }

                [RpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        GetProxy(runResult).Should().Contain("public int Echo((int, int) value)");
    }

    [Fact]
    public void DuplicateInheritedMethodsWithUnnamedLargeTupleAndValueTupleParameters_Deduplicate()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedLargeTupleAndValueTuple
            {
                public interface ILeft
                {
                    int Echo((int, int, int, int, int, int, int, int, int) value);
                }

                public interface IRight
                {
                    int Echo(System.ValueTuple<int, int, int, int, int, int, int, System.ValueTuple<int, int>> value);
                }

                [RpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        GetProxy(runResult).Should().Contain(
            "public int Echo((int, int, int, int, int, int, int, int, int) value)");
    }

    [Fact]
    public void DuplicateInheritedMethodsWithDifferentTupleReturnNames_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedTupleReturnNames
            {
                public interface ILeft
                {
                    (int A, int B) Echo();
                }

                public interface IRight
                {
                    (int X, int Y) Echo();
                }

                [RpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        AssertRejectedForTupleNames(runResult);
    }

    [Fact]
    public void DuplicateInheritedMethodsWithUnnamedTupleAndValueTupleReturns_Deduplicate()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedUnnamedTupleAndValueTupleReturns
            {
                public interface ILeft
                {
                    (int, int) Echo();
                }

                public interface IRight
                {
                    System.ValueTuple<int, int> Echo();
                }

                [RpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        GetProxy(runResult).Should().Contain("public (int, int) Echo()");
    }

    [Fact]
    public void DuplicateInheritedMethodsWithNestedGenericTupleNames_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;

            namespace Regress.DuplicateInheritedNestedTupleNames
            {
                public interface ILeft
                {
                    int Echo(List<(int A, int B)[]> values);
                }

                public interface IRight
                {
                    int Echo(List<(int X, int Y)[]> values);
                }

                [RpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        AssertRejectedForTupleNames(runResult);
    }

    private static string GetProxy(GeneratorDriverRunResult runResult) =>
        runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IFoo.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();

    private static (CSharpCompilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (compilation.AddSyntaxTrees(runResult.GeneratedTrees), runResult);
    }

    private static void AssertRejectedForTupleNames(GeneratorDriverRunResult runResult)
    {
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("incompatible tuple element names"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
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
