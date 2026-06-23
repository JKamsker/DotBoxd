using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

/// <summary>
/// Issue #47: the analyzer mines index-eligible <c>.Where(...)</c> leaves into
/// <see cref="HookSubscriptionManifest.IndexedPredicates"/> on the generated package, and that metadata
/// survives JSON export/import so a host can read it before registering.
/// </summary>
public sealed class HookChainIndexMetadataTests
{
    [Fact]
    public void Conjunction_of_indexable_leaves_is_fully_covered_and_normalized()
    {
        var manifest = GeneratedSubscription(
            """
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                .Where(e => e.MonsterId == "monster-1" && 5 >= e.Distance)
                .Select(e => e.MonsterId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        Assert.True(manifest.IndexCoversPredicate);
        Assert.Collection(
            manifest.IndexedPredicates,
            p =>
            {
                Assert.Equal("MonsterId", p.Path);
                Assert.Equal(IndexPredicateOperator.Equals, p.Operator);
                Assert.Equal("monster-1", p.Value);
                Assert.Equal("string", p.ValueType);
            },
            p =>
            {
                // "5 >= e.Distance" is normalized so the property is the left operand: Distance <= 5.
                Assert.Equal("Distance", p.Path);
                Assert.Equal(IndexPredicateOperator.LessThanOrEqual, p.Operator);
                Assert.Equal(5, Assert.IsType<int>(p.Value));
                Assert.Equal("int", p.ValueType);
            });
    }

    [Fact]
    public void Multiple_where_stages_compose_their_predicates()
    {
        var manifest = GeneratedSubscription(
            """
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                .Where(e => e.Distance > 0)
                .Where(e => e.MonsterId != "boss")
                .Select(e => e.MonsterId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        Assert.True(manifest.IndexCoversPredicate);
        Assert.Equal(2, manifest.IndexedPredicates.Count);
        Assert.Equal(IndexPredicateOperator.GreaterThan, manifest.IndexedPredicates[0].Operator);
        Assert.Equal("Distance", manifest.IndexedPredicates[0].Path);
        Assert.Equal(IndexPredicateOperator.NotEquals, manifest.IndexedPredicates[1].Operator);
        Assert.Equal("MonsterId", manifest.IndexedPredicates[1].Path);
    }

    [Fact]
    public void An_or_branch_keeps_the_necessary_leaf_but_marks_partial_coverage()
    {
        var manifest = GeneratedSubscription(
            """
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                .Where(e => e.Distance <= 5 && (e.Distance > 0 || e.MonsterId == "monster-1"))
                .Select(e => e.MonsterId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        // The leading AND leaf is a necessary condition, so it is a safe prefilter; the OR branch is not
        // indexable, so coverage is only partial and the verified IR must still run.
        Assert.False(manifest.IndexCoversPredicate);
        var predicate = Assert.Single(manifest.IndexedPredicates);
        Assert.Equal("Distance", predicate.Path);
        Assert.Equal(IndexPredicateOperator.LessThanOrEqual, predicate.Operator);
    }

    [Fact]
    public void A_non_constant_predicate_yields_no_index_metadata()
    {
        var manifest = GeneratedSubscription(
            """
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                .Where(e => e.MonsterId.Length > 3)
                .Select(e => e.MonsterId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        Assert.False(manifest.IndexCoversPredicate);
        Assert.Empty(manifest.IndexedPredicates);
    }

    [Fact]
    public void Index_metadata_survives_json_round_trip()
    {
        var package = GeneratedPackage(
            """
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                .Where(e => e.MonsterId == "monster-1" && e.Distance <= 5)
                .Select(e => e.MonsterId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        var imported = PluginPackageJsonSerializer.Import(PluginPackageJsonSerializer.Export(package));
        var subscription = Assert.Single(imported.Manifest.Subscriptions);
        Assert.True(subscription.IndexCoversPredicate);
        Assert.Equal(
            package.Manifest.Subscriptions[0].IndexedPredicates,
            subscription.IndexedPredicates);
    }

    [Fact]
    public void A_chain_without_index_metadata_omits_the_json_keys()
    {
        var package = GeneratedPackage(
            """
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                .Where(e => e.MonsterId.Length > 3)
                .Select(e => e.MonsterId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        var json = PluginPackageJsonSerializer.Export(package);
        Assert.DoesNotContain("indexedPredicates", json, StringComparison.Ordinal);
        Assert.DoesNotContain("indexCoversPredicate", json, StringComparison.Ordinal);

        // ...and it still round-trips to an empty, non-covering index.
        var imported = PluginPackageJsonSerializer.Import(json);
        Assert.False(imported.Manifest.Subscriptions[0].IndexCoversPredicate);
        Assert.Empty(imported.Manifest.Subscriptions[0].IndexedPredicates);
    }

    [Fact]
    public void Importing_a_null_indexed_predicate_value_is_rejected()
    {
        var package = GeneratedPackage(
            """
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                .Where(e => e.MonsterId == "needle-value")
                .Select(e => e.MonsterId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        // Null is not a valid index value (the schema forbids it); the importer must reject it rather than
        // carry a null the host can never match.
        var json = PluginPackageJsonSerializer.Export(package)
            .Replace("\"needle-value\"", "null", StringComparison.Ordinal);

        var ex = Assert.Throws<DotBoxD.Kernels.Model.SandboxValidationException>(
            () => PluginPackageJsonSerializer.Import(json));
        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-VALUE");
    }

    private static HookSubscriptionManifest GeneratedSubscription(string chain)
        => GeneratedPackage(chain).Manifest.Subscriptions.Single();

    private static PluginPackage GeneratedPackage(string chain)
    {
        var source = $$"""
            using DotBoxD.Plugins.Runtime;

            namespace ChainSample;

            public static class Usage
            {
                public static void Configure(SubscriptionRegistry subscriptions)
                    => {{chain}}
            }
            """;

        var assembly = Compile(source);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes) is not null);
        var create = packageType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!;
        return (PluginPackage)create.Invoke(null, null)!;
    }

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDIndexMetadataTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(Runtime.ChainAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
