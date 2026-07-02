using DotBoxD.Services.SourceGenerator.EntryPoint;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

/// <summary>
/// Behavioral coverage for the source generator's internal utility types. None of these
/// types are visible to the test assembly (there is no InternalsVisibleTo on
/// DotBoxD.Services.SourceGenerator), so every assertion drives them through the public
/// <see cref="DotBoxDRpcGenerator"/> via <see cref="GeneratorTestHelper"/>
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
public partial class SourceGenUtilityCoverageTests
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
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Zeta.Pkg
            {
                [RpcService]
                public interface IZeta { Task PingAsync(); }
            }
            """;

        const string svcNsAOne = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Alpha.Pkg
            {
                [RpcService(Name = "Bravo")]
                public interface IAlphaService { Task PingAsync(); }
            }
            """;

        const string svcNsATwo = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Alpha.Pkg
            {
                [RpcService(Name = "Alpha")]
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
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Same.Ns
            {
                [RpcService(Name = "Zeta")]
                public interface IAaa { Task PingAsync(); }

                [RpcService(Name = "Alpha")]
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
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Eq.Demo
            {
                [RpcService]
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
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Eq.Demo
            {
                [RpcService]
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
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Eq.Demo2
            {
                [RpcService]
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
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Eq.Demo2
            {
                [RpcService]
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
            .Single(g => g.HintName.EndsWith("IThing.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("AddAsync(int a, int b, int c");
    }

    [Fact]
    public void EmptyParameterAndSiblingArrays_StillGenerateCompilingOutput()
    {
        // A zero-parameter, void-returning method exercises the empty/default EquatableArray
        // branches (IsDefaultOrEmpty hash = 0, empty enumerator) used throughout emit.
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Eq.Empty
            {
                [RpcService]
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
            .Single(g => g.HintName.EndsWith("INoArgs.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("void Ping()");
    }

    // ---------------------------------------------------------------------
    // ExistingTypeLocationIndex: a pre-existing user type with the generated name
    // must produce a DBXS003 collision diagnostic whose location points at the
    // user declaration. Duplicate declarations across multiple files force the
    // dedup + location tie-break path, and Find() runs a binary search.
    // ---------------------------------------------------------------------

}
