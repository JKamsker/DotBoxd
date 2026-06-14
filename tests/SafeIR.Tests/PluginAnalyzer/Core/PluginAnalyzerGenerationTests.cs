using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerGenerationTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generator_uses_interface_method_implementation_not_first_same_named_method()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("method-selection")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                private bool ShouldHandle() => false;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");
            }
            """);

        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("new global::SafeIR.IfStatement(Bool(true)", generated);
        Assert.DoesNotContain("new global::SafeIR.IfStatement(Bool(false)", generated);
    }

    [Fact]
    public void Generator_preserves_constant_live_setting_default_expressions()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("constant-defaults")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int MinDamage { get; set; } = 50 + 50;

                [LiveSetting]
                public int Offset { get; set; } = -1;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");
            }
            """);

        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"MinDamage\", \"int\", 100)", generated);
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"Offset\", \"int\", -1)", generated);
    }

    [Fact]
    public void Generator_reports_nonconstant_live_setting_default()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("bad-default")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int MinDamage { get; set; } = Compute();

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");

                private static int Compute() => 100;
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("double.NaN")]
    [InlineData("double.PositiveInfinity")]
    [InlineData("double.NegativeInfinity")]
    public void Generator_reports_non_finite_double_live_setting_default(string defaultValue)
    {
        var result = RunGenerator($$"""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("bad-double-default")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public double Ratio { get; set; } = {{defaultValue}};

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_orders_event_parameters_like_convention_adapter()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed class DamageEvent
            {
                public int Amount { get; }
                public string DamageType { get; }
                public string TargetId { get; }

                public DamageEvent(string damageType, int amount, string targetId)
                {
                    DamageType = damageType;
                    Amount = amount;
                    TargetId = targetId;
                }
            }

            [Plugin("event-order")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => e.Amount > 0;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.DamageType);
            }
            """);

        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.True(
            generated.IndexOf("parameters[0] = new global::SafeIR.Parameter(\"e_DamageType\"", StringComparison.Ordinal) <
            generated.IndexOf("parameters[1] = new global::SafeIR.Parameter(\"e_Amount\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_reports_game_plugin_without_event_kernel_contract()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            [Plugin("wrong-contract")]
            public sealed partial class DamageFilter
            {
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("[LiveSetting] private int Hidden { get; set; } = 1;")]
    [InlineData("[LiveSetting] public static int StaticValue { get; set; } = 1;")]
    [InlineData("[LiveSetting] public int GetOnly { get; } = 1;")]
    [InlineData("[LiveSetting] public int InitOnly { get; init; } = 1;")]
    public void Generator_rejects_live_settings_that_runtime_typed_state_cannot_update(string property)
    {
        var result = RunGenerator($$"""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("bad-live-setting")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                {{property}}

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_rejects_duplicate_live_setting_names_from_base_type()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public class BaseKernel
            {
                [LiveSetting]
                public int MinDamage { get; set; } = 1;
            }

            [Plugin("duplicate-live-setting")]
            public sealed partial class DamageKernel : BaseKernel, IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public new int MinDamage { get; set; } = 2;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "SafeIrPluginGenerationTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
