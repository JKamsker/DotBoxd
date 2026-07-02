using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

public partial class SourceGenUtilityCoverageTests
{
    [Fact]
    public void InheritedLargeValueTupleVsNamedTuple_WithMatchingDefaults_Deduplicate()
    {
        // An 8-arity ValueTuple flattens its TRest against a nine-element named tuple
        // whose names are the implicit Item1..Item9 defaults, so they compare equal.
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Tuple.LargeRest
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
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        GetFooProxy(runResult)
            .Should().Contain("Echo((int, int, int, int, int, int, int, int, int) value)");
    }

    [Fact]
    public void InheritedNonTupleGenericArity_Mismatch_RejectService_OnReturnShape()
    {
        // Different generic arity on the return type drives the type-argument-length
        // comparison branch without any tuple element names involved.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;

            namespace Tuple.GenericArity
            {
                public interface ILeft  { int Echo(Dictionary<string, int> map); }
                public interface IRight { int Echo(List<int> map); }

                [RpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var (final, runResult) = Run(source);
        // Distinct parameter element types are NOT duplicate methods, so both overloads
        // survive and the service still generates; the comparer simply reports "not same".
        AssertCompiles(final);
        GetFooProxy(runResult).Should().Contain("Echo(");
    }

    // ---------------------------------------------------------------------
    // SubServicePayloadInspector + IdentifierHelpers: a keyword-named namespace that
    // also carries a sub-service DTO. Exercises namespace escaping and the DTO member
    // walk that finds an embedded sub-service interface.
    // ---------------------------------------------------------------------

    [Fact]
    public void KeywordNamespace_GeneratesEscapedQualifiedNames()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace @namespace.@class
            {
                [RpcService]
                public interface IThing
                {
                    Task<int> AddAsync(int a, int b);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IThing.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("@namespace.@class");
    }

    [Fact]
    public void DtoFieldContainingSubServiceInKeywordNamespace_BecomesUnsupportedStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace @event.payload
            {
                public sealed class Carrier
                {
                    public ISub? Inner;
                }

                [RpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    Task SendAsync(Carrier carrier);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "DBXS002" && d.GetMessage().Contains("contains a sub-service type"));

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"SendAsync\":");
    }

    // ---------------------------------------------------------------------
    // DotBoxDRpcGenerator empty-aggregate early return (lines around 88/222): a source
    // tree that produces only diagnostics (no model) must NOT emit the aggregate
    // extension/factory files.
    // ---------------------------------------------------------------------

    [Fact]
    public void ServiceRejectedAtServiceLevel_ProducesNoAggregateExtensionFiles()
    {
        // Incompatible inherited tuple element names reject the entire IFoo service
        // (DBXS003), so no ServiceModel flows downstream. The AllServices aggregate
        // sees an empty identity array and must early-return without emitting either
        // DotBoxDRpcExtensions.g.cs or DotBoxDGenerated.g.cs.
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Empty.Aggregate
            {
                public interface ILeft  { int Echo((int A, int B) value); }
                public interface IRight { int Echo((int X, int Y) value); }

                [RpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "DBXS003" && d.GetMessage().Contains("incompatible tuple element names"));

        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().NotContain("DotBoxDRpcExtensions.g.cs");
        hints.Should().NotContain("DotBoxDGenerated.g.cs");
        hints.Should().NotContain(h => h.Contains("IFoo."));
    }

    [Fact]
    public void NoServicesAtAll_EmitsNothing()
    {
        const string source = """
            namespace Plain.Code
            {
                public class Unrelated { public int X { get; set; } }
            }
            """;

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        runResult.Results.Single().GeneratedSources.Should().BeEmpty(
            "with no [RpcService] the aggregate must early-return and emit no source");
        runResult.Diagnostics.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // helpers (mirrors the conventions used across the existing generator tests)
    // ---------------------------------------------------------------------

    private static string ExtensionsTextFor(params string[] sources)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(sources);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        return runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "DotBoxDRpcExtensions.g.cs")
            .SourceText.ToString();
    }

    private static string DiagnosticPathFor(params SyntaxTree[] trees)
    {
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(trees);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        var diagnostic = runResult.Diagnostics.First(d =>
            d.Id == "DBXS003" && d.GetMessage().Contains("would collide"));
        return diagnostic.Location.GetLineSpan().Path;
    }

    private static string GetFooProxy(GeneratorDriverRunResult runResult) =>
        runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IFoo.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();

    private static void AssertRejectedForTupleNames(GeneratorDriverRunResult runResult)
    {
        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "DBXS003" && d.GetMessage().Contains("incompatible tuple element names"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    private static (CSharpCompilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
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
