using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

/// <summary>
/// Issue #51 (candidate 1): the analyzer mines index-eligible <c>event-property &lt;op&gt; constant</c> leaves
/// from a kernel-class <c>ShouldHandle</c> body the same way it does from an inline <c>.Where(...)</c> chain,
/// emitting them onto <see cref="HookSubscriptionManifest.IndexedPredicates"/>. Non-constant comparisons
/// (live settings, captured fields), <c>||</c>/<c>!</c>, and multi-statement bodies stay conservatively
/// non-indexed so the verified IR remains the authority.
/// </summary>
public sealed class KernelClassIndexMetadataTests
{
    [Fact]
    public void Should_handle_conjunction_of_constants_is_fully_covered_and_normalized()
    {
        var manifest = GeneratedSubscription(
            """
            public bool ShouldHandle(DamageEvent e, HookContext ctx)
                => e.DamageType == "fire" && 5 <= e.Amount;
            """);

        Assert.True(manifest.IndexCoversPredicate);
        Assert.Collection(
            manifest.IndexedPredicates,
            p =>
            {
                Assert.Equal("DamageType", p.Path);
                Assert.Equal(IndexPredicateOperator.Equals, p.Operator);
                Assert.Equal("fire", p.Value);
                Assert.Equal("string", p.ValueType);
            },
            p =>
            {
                // "5 <= e.Amount" is normalized so the property is the left operand: Amount >= 5.
                Assert.Equal("Amount", p.Path);
                Assert.Equal(IndexPredicateOperator.GreaterThanOrEqual, p.Operator);
                Assert.Equal(5, Assert.IsType<int>(p.Value));
                Assert.Equal("int", p.ValueType);
            });
    }

    [Fact]
    public void Single_return_block_body_is_indexed()
    {
        var manifest = GeneratedSubscription(
            """
            public bool ShouldHandle(DamageEvent e, HookContext ctx)
            {
                return e.Amount >= 5;
            }
            """);

        Assert.True(manifest.IndexCoversPredicate);
        var predicate = Assert.Single(manifest.IndexedPredicates);
        Assert.Equal("Amount", predicate.Path);
        Assert.Equal(IndexPredicateOperator.GreaterThanOrEqual, predicate.Operator);
        Assert.Equal(5, Assert.IsType<int>(predicate.Value));
    }

    [Fact]
    public void An_or_branch_keeps_the_necessary_leaf_but_marks_partial_coverage()
    {
        var manifest = GeneratedSubscription(
            """
            public bool ShouldHandle(DamageEvent e, HookContext ctx)
                => e.Amount >= 5 && (e.Amount < 100 || e.DamageType == "fire");
            """);

        Assert.False(manifest.IndexCoversPredicate);
        var predicate = Assert.Single(manifest.IndexedPredicates);
        Assert.Equal("Amount", predicate.Path);
        Assert.Equal(IndexPredicateOperator.GreaterThanOrEqual, predicate.Operator);
    }

    [Fact]
    public void A_live_setting_comparison_yields_no_index_metadata()
    {
        // MinDamage is a [LiveSetting], not a compile-time constant, so the comparison is not index-eligible
        // and the whole predicate stays verified-IR only — exactly the pre-#51 behavior for such kernels.
        var manifest = GeneratedSubscription(
            """
            [LiveSetting]
            public int MinDamage { get; set; } = 5;

            public bool ShouldHandle(DamageEvent e, HookContext ctx)
                => e.Amount >= MinDamage;
            """);

        Assert.False(manifest.IndexCoversPredicate);
        Assert.Empty(manifest.IndexedPredicates);
    }

    [Fact]
    public void An_always_true_should_handle_yields_no_index_metadata()
    {
        var manifest = GeneratedSubscription(
            """
            public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;
            """);

        Assert.False(manifest.IndexCoversPredicate);
        Assert.Empty(manifest.IndexedPredicates);
    }

    private static HookSubscriptionManifest GeneratedSubscription(string kernelBody)
        => GeneratedPackage(kernelBody).Manifest.Subscriptions.Single();

    private static PluginPackage GeneratedPackage(string kernelBody)
    {
        var source = $$"""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace KernelSample;

            public sealed record DamageEvent(string DamageType, int Amount, string TargetId);

            [Plugin("index-sample")]
            public sealed partial class IndexSampleKernel : IEventKernel<DamageEvent>
            {
                {{kernelBody}}

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "ouch");
            }
            """;

        var assembly = Compile(source);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal) &&
            type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes) is not null);
        var create = packageType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!;
        return (PluginPackage)create.Invoke(null, null)!;
    }

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "DotBoxDKernelIndexMetadataTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
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
