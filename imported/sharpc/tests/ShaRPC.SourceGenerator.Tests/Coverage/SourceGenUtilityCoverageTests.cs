using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.SourceGenerator.Tests;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Behavioral coverage for the source generator's internal utility types. None of these
/// types are visible to the test assembly (there is no InternalsVisibleTo on
/// ShaRPC.SourceGenerator), so every assertion drives them through the public
/// <see cref="ShaRPC.SourceGenerator.ShaRpcGenerator"/> via <see cref="GeneratorTestHelper"/>
/// and inspects the observable result: generated source text, reported diagnostics, and
/// incremental step caching reasons.
///
/// The covered utilities and the scenarios that force them:
/// <list type="bullet">
/// <item>EquatableArray&lt;T&gt; — value equality / GetHashCode across incremental re-runs and
/// enumeration during emit.</item>
/// <item>ServiceModelOrdering — deterministic ordering of several services.</item>
/// <item>FinalRejectionMethodParameters — final-rejected sub-service stubs (CT present vs synthesized).</item>
/// <item>ExistingTypeLocationIndex — pre-existing user types that collide with generated names,
/// including duplicate keys and tie-broken locations driving the binary-search Find.</item>
/// <item>TupleElementNameComparer — inherited duplicate methods with named/unnamed tuples,
/// arrays, nested generics, and large ValueTuples.</item>
/// <item>SubServicePayloadInspector / IdentifierHelpers — sub-service DTO detection and keyword
/// namespace escaping.</item>
/// </list>
/// </summary>
public class SourceGenUtilityCoverageTests
{
    // ---------------------------------------------------------------------
    // ServiceModelOrdering: deterministic ordering across all three tie-break
    // levels (namespace, interface name, configured service name).
    // ---------------------------------------------------------------------

    [Fact]
    public void MultipleServices_AreOrderedDeterministically_RegardlessOfSyntaxTreeOrder()
    {
        // Three services across two namespaces with one service-name override; sorting
        // must hit the namespace, interface-name, and service-name comparators.
        const string svcNsB = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Zeta.Pkg
            {
                [ShaRpcService]
                public interface IZeta { Task PingAsync(); }
            }
            """;

        const string svcNsAOne = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Alpha.Pkg
            {
                [ShaRpcService(Name = "Bravo")]
                public interface IAlphaService { Task PingAsync(); }
            }
            """;

        const string svcNsATwo = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Alpha.Pkg
            {
                [ShaRpcService(Name = "Alpha")]
                public interface IBetaService { Task PingAsync(); }
            }
            """;

        var forward = ExtensionsTextFor(svcNsB, svcNsAOne, svcNsATwo);
        var reversed = ExtensionsTextFor(svcNsATwo, svcNsAOne, svcNsB);

        reversed.Should().Be(forward,
            "service ordering must be a pure function of service identity, not syntax-tree order");

        // Alpha.Pkg must be emitted before Zeta.Pkg (namespace comparator).
        forward.IndexOf("Alpha", StringComparison.Ordinal)
            .Should().BeLessThan(forward.IndexOf("Zeta", StringComparison.Ordinal));
    }

    [Fact]
    public void ServicesSharingNamespaceAndInterfacePrefix_OrderByConfiguredServiceName()
    {
        // Same namespace, distinct interface names so the interface-name comparator decides,
        // plus name overrides so the service-name comparator participates in factory output.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Same.Ns
            {
                [ShaRpcService(Name = "Zeta")]
                public interface IAaa { Task PingAsync(); }

                [ShaRpcService(Name = "Alpha")]
                public interface IBbb { Task PingAsync(); }
            }
            """;

        var first = ExtensionsTextFor(source);
        var second = ExtensionsTextFor(source);

        // Deterministic across independent compilations.
        second.Should().Be(first);
        // IAaa sorts before IBbb on interface name regardless of the service-name override.
        first.IndexOf("IAaa", StringComparison.Ordinal)
            .Should().BeLessThan(first.IndexOf("IBbb", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // EquatableArray<T>: value equality drives incremental caching. A trivia-only
    // edit must keep the per-service model (which embeds several EquatableArray
    // fields) cached; a real signature change must invalidate it. This exercises
    // EquatableArray.Equals / == / GetHashCode on the non-empty path, and the
    // enumerator during code emission.
    // ---------------------------------------------------------------------

    [Fact]
    public void TriviaOnlyEdit_KeepsModelCached_ProvingEquatableArrayValueEquality()
    {
        const string original = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Eq.Demo
            {
                [ShaRpcService]
                public interface IThing
                {
                    Task<int> AddAsync(int a, int b);
                    Task PingAsync();
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(original);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(tree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var withComment = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Eq.Demo
            {
                [ShaRpcService]
                public interface IThing
                {
                    // trivia-only edit: identical symbols, identical EquatableArray contents
                    Task<int> AddAsync(int a, int b);
                    Task PingAsync();
                }
            }
            """);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(tree, withComment));
        var result = driver.GetRunResult();

        var servicesOutputs = result.Results.Single().TrackedSteps["Services"]
            .SelectMany(s => s.Outputs)
            .ToArray();

        servicesOutputs.Should().NotBeEmpty();
        servicesOutputs.Should().OnlyContain(
            o => o.Reason == IncrementalStepRunReason.Cached
              || o.Reason == IncrementalStepRunReason.Unchanged,
            "the model's EquatableArray fields must compare value-equal so the model stays cached");
    }

    [Fact]
    public void ParameterListEdit_InvalidatesModel_ProvingEquatableArrayInequality()
    {
        const string original = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Eq.Demo2
            {
                [ShaRpcService]
                public interface IThing
                {
                    Task<int> AddAsync(int a, int b);
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(original);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(tree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        // Add a parameter: the parameter EquatableArray now differs in length/content.
        var changed = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Eq.Demo2
            {
                [ShaRpcService]
                public interface IThing
                {
                    Task<int> AddAsync(int a, int b, int c);
                }
            }
            """);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(tree, changed));
        var result = driver.GetRunResult();

        var servicesOutputs = result.Results.Single().TrackedSteps["Services"]
            .SelectMany(s => s.Outputs)
            .ToArray();

        servicesOutputs.Any(o =>
                o.Reason == IncrementalStepRunReason.Modified
             || o.Reason == IncrementalStepRunReason.New)
            .Should().BeTrue("a changed parameter list must make the Equatable<parameter> array unequal");

        var proxy = result.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IThing.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("AddAsync(int a, int b, int c");
    }

    [Fact]
    public void EmptyParameterAndSiblingArrays_StillGenerateCompilingOutput()
    {
        // A zero-parameter, void-returning method exercises the empty/default EquatableArray
        // branches (IsDefaultOrEmpty hash = 0, empty enumerator) used throughout emit.
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Eq.Empty
            {
                [ShaRpcService]
                public interface INoArgs
                {
                    void Ping();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("INoArgs.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("void Ping()");
    }

    // ---------------------------------------------------------------------
    // ExistingTypeLocationIndex: a pre-existing user type with the generated name
    // must produce a SHARPC003 collision diagnostic whose location points at the
    // user declaration. Duplicate declarations across multiple files force the
    // dedup + location tie-break path, and Find() runs a binary search.
    // ---------------------------------------------------------------------

    [Fact]
    public void ExistingTypeCollision_ReportsDiagnosticAtUserTypeLocation()
    {
        var collision = CSharpSyntaxTree.ParseText("""
            namespace Collide.Single
            {
                public sealed class ThingProxy { }
            }
            """, path: "UserType.cs");

        var service = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;

            namespace Collide.Single
            {
                [ShaRpcService]
                public interface IThing { int Bar(); }
            }
            """, path: "Service.cs");

        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(collision, service);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        var diagnostic = runResult.Diagnostics.Single(d =>
            d.Id == "SHARPC003" &&
            d.GetMessage().Contains("generated proxy type 'ThingProxy' would collide"));
        diagnostic.Location.GetLineSpan().Path.Should().Be("UserType.cs",
            "the collision location must come from the pre-existing user declaration");
    }

    [Fact]
    public void DuplicateExistingTypeDeclarations_DedupAndPickStableLocation()
    {
        // Two identical declarations of the colliding type in different files. The index
        // must dedup by key and break the location tie deterministically by file path,
        // so the diagnostic anchors to "A.cs" no matter the syntax-tree order.
        const string colliding = """
            namespace Collide.Dup
            {
                public sealed class ThingProxy { }
            }
            """;
        var bCs = CSharpSyntaxTree.ParseText(colliding, path: "B.cs");
        var aCs = CSharpSyntaxTree.ParseText(colliding, path: "A.cs");
        var service = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;

            namespace Collide.Dup
            {
                [ShaRpcService]
                public interface IThing { int Bar(); }
            }
            """, path: "Service.cs");

        var forward = DiagnosticPathFor(bCs, service, aCs);
        var reversed = DiagnosticPathFor(aCs, service, bCs);

        forward.Should().Be("A.cs");
        reversed.Should().Be("A.cs",
            "duplicate existing-type locations must tie-break by path regardless of order");
    }

    [Fact]
    public void MultipleDistinctCollisions_AllResolveViaLocationIndexBinarySearch()
    {
        // Several distinct collisions exercise Find()'s binary search across a sorted,
        // de-duplicated index where the search target sits at varied positions.
        var collisions = CSharpSyntaxTree.ParseText("""
            namespace Collide.Many
            {
                public sealed class AaaProxy { }
                public sealed class MmmProxy { }
                public sealed class ZzzProxy { }
            }
            """, path: "Existing.cs");

        var services = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;

            namespace Collide.Many
            {
                [ShaRpcService]
                public interface IAaa { int A(); }

                [ShaRpcService]
                public interface IMmm { int M(); }

                [ShaRpcService]
                public interface IZzz { int Z(); }
            }
            """, path: "Services.cs");

        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(collisions, services);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        foreach (var name in new[] { "AaaProxy", "MmmProxy", "ZzzProxy" })
        {
            var diagnostic = runResult.Diagnostics.Single(d =>
                d.Id == "SHARPC003" &&
                d.GetMessage().Contains($"generated proxy type '{name}' would collide"));
            diagnostic.Location.GetLineSpan().Path.Should().Be("Existing.cs");
        }
    }

    // ---------------------------------------------------------------------
    // FinalRejectionMethodParameters: a sub-service that gets finally rejected makes
    // the root method a NotSupported stub. The parameter builder runs in two modes:
    // an async method that already declares a CancellationToken (early return of the
    // existing list) and a method that needs a synthesized trailing `ct`.
    // ---------------------------------------------------------------------

    [Fact]
    public void FinalRejectedSubService_WithExistingCancellationToken_StubsRootMethod()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace FinalReject.WithCt
            {
                // Pre-existing async-sibling interface forces a generated-name collision that
                // finally rejects ISub, which in turn stubs IRoot.OpenAsync.
                public interface ISubAsync { }

                [ShaRpcService]
                public interface ISub
                {
                    int Count();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<ISub> OpenAsync(int id, CancellationToken ct = default);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "SHARPC002" && d.GetMessage().Contains("IRoot.OpenAsync"));

        var rootProxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        rootProxy.Should().Contain("throw new global::System.NotSupportedException");
        // The original CancellationToken parameter must be preserved on the stub signature.
        rootProxy.Should().Contain("OpenAsync(int id");
        rootProxy.Should().Contain("global::System.Threading.CancellationToken");
    }

    [Fact]
    public void FinalRejectedSubService_WithoutCancellationToken_StubSynthesizesTrailingCt()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace FinalReject.NoCt
            {
                public interface ISubAsync { }

                [ShaRpcService]
                public interface ISub
                {
                    int Count();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    // No CancellationToken declared: the stub builder must synthesize a
                    // trailing ct parameter for the generated async sibling stub.
                    Task<ISub> OpenAsync(int id);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "SHARPC002" && d.GetMessage().Contains("IRoot.OpenAsync"));

        var rootProxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        rootProxy.Should().Contain("throw new global::System.NotSupportedException");
        rootProxy.Should().Contain("OpenAsync(int id");
    }

    // ---------------------------------------------------------------------
    // TupleElementNameComparer: inherited duplicate methods with tuple types. These
    // exercise array element-name comparison, nested generic descent, large
    // ValueTuple flattening (TRest), explicit-vs-default element name comparison, and
    // the mismatch rejection path.
    // ---------------------------------------------------------------------

    [Fact]
    public void InheritedArrayTupleParameters_WithMatchingNames_Deduplicate()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Tuple.Array
            {
                public interface ILeft  { int Echo((int A, int B)[] values); }
                public interface IRight { int Echo((int A, int B)[] values); }

                [ShaRpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC003");
        GetFooProxy(runResult).Should().Contain("Echo((int A, int B)[] values)");
    }

    [Fact]
    public void InheritedArrayTupleParameters_WithDifferentNames_RejectService()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Tuple.ArrayMismatch
            {
                public interface ILeft  { int Echo((int A, int B)[] values); }
                public interface IRight { int Echo((int X, int Y)[] values); }

                [ShaRpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var (_, runResult) = Run(source);
        AssertRejectedForTupleNames(runResult);
    }

    [Fact]
    public void InheritedNestedGenericTupleReturns_WithDifferentNames_RejectService()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Collections.Generic;

            namespace Tuple.NestedGeneric
            {
                public interface ILeft  { Dictionary<string, (int A, int B)> Echo(); }
                public interface IRight { Dictionary<string, (int X, int Y)> Echo(); }

                [ShaRpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var (_, runResult) = Run(source);
        AssertRejectedForTupleNames(runResult);
    }

    [Fact]
    public void InheritedLargeValueTupleVsNamedTuple_WithMatchingDefaults_Deduplicate()
    {
        // An 8-arity ValueTuple flattens its TRest against a nine-element named tuple
        // whose names are the implicit Item1..Item9 defaults, so they compare equal.
        const string source = """
            using ShaRPC.Core.Attributes;

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

                [ShaRpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC003");
        GetFooProxy(runResult)
            .Should().Contain("Echo((int, int, int, int, int, int, int, int, int) value)");
    }

    [Fact]
    public void InheritedNonTupleGenericArity_Mismatch_RejectService_OnReturnShape()
    {
        // Different generic arity on the return type drives the type-argument-length
        // comparison branch without any tuple element names involved.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Collections.Generic;

            namespace Tuple.GenericArity
            {
                public interface ILeft  { int Echo(Dictionary<string, int> map); }
                public interface IRight { int Echo(List<int> map); }

                [ShaRpcService]
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
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace @namespace.@class
            {
                [ShaRpcService]
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
            .Single(g => g.HintName.EndsWith("IThing.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("@namespace.@class");
    }

    [Fact]
    public void DtoFieldContainingSubServiceInKeywordNamespace_BecomesUnsupportedStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace @event.payload
            {
                public sealed class Carrier
                {
                    public ISub? Inner;
                }

                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task SendAsync(Carrier carrier);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "SHARPC002" && d.GetMessage().Contains("contains a sub-service type"));

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"SendAsync\":");
    }

    // ---------------------------------------------------------------------
    // ShaRpcGenerator empty-aggregate early return (lines around 88/222): a source
    // tree that produces only diagnostics (no model) must NOT emit the aggregate
    // extension/factory files.
    // ---------------------------------------------------------------------

    [Fact]
    public void ServiceRejectedAtServiceLevel_ProducesNoAggregateExtensionFiles()
    {
        // Incompatible inherited tuple element names reject the entire IFoo service
        // (SHARPC003), so no ServiceModel flows downstream. The AllServices aggregate
        // sees an empty identity array and must early-return without emitting either
        // ShaRpcExtensions.g.cs or ShaRpcGenerated.g.cs.
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Empty.Aggregate
            {
                public interface ILeft  { int Echo((int A, int B) value); }
                public interface IRight { int Echo((int X, int Y) value); }

                [ShaRpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "SHARPC003" && d.GetMessage().Contains("incompatible tuple element names"));

        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().NotContain("ShaRpcExtensions.g.cs");
        hints.Should().NotContain("ShaRpcGenerated.g.cs");
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
            "with no [ShaRpcService] the aggregate must early-return and emit no source");
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
            .Single(g => g.HintName == "ShaRpcExtensions.g.cs")
            .SourceText.ToString();
    }

    private static string DiagnosticPathFor(params SyntaxTree[] trees)
    {
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(trees);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        var diagnostic = runResult.Diagnostics.First(d =>
            d.Id == "SHARPC003" && d.GetMessage().Contains("would collide"));
        return diagnostic.Location.GetLineSpan().Path;
    }

    private static string GetFooProxy(GeneratorDriverRunResult runResult) =>
        runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IFoo.ShaRpcProxy.g.cs"))
            .SourceText.ToString();

    private static void AssertRejectedForTupleNames(GeneratorDriverRunResult runResult)
    {
        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "SHARPC003" && d.GetMessage().Contains("incompatible tuple element names"));
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
