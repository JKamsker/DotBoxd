using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerRangeCompletenessTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generator_preserves_supported_numeric_range_overloads()
    {
        var result = RunGenerator("""
            using System.ComponentModel.DataAnnotations;
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            [Plugin("ranges")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                [Range(0, 100)]
                public int MinDamage { get; set; } = 50;

                [LiveSetting]
                [Range(0.5D, 2.5D)]
                public double Ratio { get; set; } = 1.5D;

                [LiveSetting]
                [Range(typeof(long), "1", "9")]
                public long Sequence { get; set; } = 7L;

                [LiveSetting]
                [Range(1L, 10000000000L)]
                public long LargeSequence { get; set; } = 7L;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"MinDamage\", \"int\", 50, 0, 100)", generated);
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"Ratio\", \"double\", 1.5D, 0.5D, 2.5D)", generated);
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"Sequence\", \"long\", 7L, 1L, 9L)", generated);
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"LargeSequence\", \"long\", 7L, 1L, 10000000000L)", generated);
    }

    [Theory]
    [InlineData("[LiveSetting][Range(0, 10)] public string Text { get; set; } = \"x\";")]
    [InlineData("[LiveSetting][Range(10, 0)] public int MinDamage { get; set; } = 5;")]
    [InlineData("[LiveSetting][Range(0.5D, 10.5D)] public int MinDamage { get; set; } = 5;")]
    [InlineData("[LiveSetting][Range(typeof(int), \"bad\", \"10\")] public int MinDamage { get; set; } = 5;")]
    [InlineData("[LiveSetting][Range(typeof(long), \"9223372036854775807\", \"9223372036854775806\")] public long Sequence { get; set; } = 7L;")]
    [InlineData("[LiveSetting][Range(typeof(string), \"0\", \"10\")] public int MinDamage { get; set; } = 5;")]
    public void Generator_reports_live_setting_ranges_that_cannot_lower_to_valid_manifest(string property)
    {
        var result = RunGenerator($$"""
            using System.ComponentModel.DataAnnotations;
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            [Plugin("bad-range")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                {{property}}

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "SafeIrPluginRangeCompletenessTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var result = driver.GetRunResult();
        if (result.GeneratedTrees.Length > 0)
        {
            Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
            Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        }

        return result;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
