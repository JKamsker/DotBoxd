using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SafeIR;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed partial class PluginAnalyzerTests
{
    [Fact]
    public void Generates_fire_damage_plugin_package_from_kernel_class()
    {
        var compilation = CreateCompilation("""
            using System.ComponentModel.DataAnnotations;
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string DamageType, int Amount, string TargetId);

            [Plugin("fire-damage")]
            public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public string DamageType { get; set; } = "fire";

                [LiveSetting]
                [Range(0, 10_000)]
                public int MinDamage { get; set; } = 100;

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.DamageType == DamageType &&
                       e.Amount >= MinDamage;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "Ouch, fire.");
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generatedTree = Assert.Single(driver.GetRunResult().GeneratedTrees);
        var generated = generatedTree.GetText().ToString();

        Assert.Contains("public static class FireDamagePluginPackage", generated);
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"DamageType\", \"string\", \"fire\")", generated);
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"MinDamage\", \"int\", 100, 0, 10000)", generated);
        Assert.Contains("new global::SafeIR.IfStatement(StringEquals(Var(\"e_DamageType\"), Var(\"DamageType\"))", generated);
        Assert.Contains("global::SafeIR.Plugins.PluginMessageBindings.SendBindingId", generated);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
    }

    [Fact]
    public void Generator_reports_unsupported_kernel_shape_as_diagnostic()
    {
        var compilation = CreateCompilation("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string DamageType);

            [Plugin("bad-shape")]
            public sealed partial class BadKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => IsFire(e);

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send("player-1", "message");

                private static bool IsFire(DamageEvent e) => e.DamageType == "fire";
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, d => d.Id == "SGP100");
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public void Generator_reports_block_bodied_should_handle_with_extra_statements()
    {
        var compilation = CreateCompilation("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string DamageType);

            [Plugin("bad-block")]
            public sealed partial class BadKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                {
                    var matched = e.DamageType == "fire";
                    return matched;
                }

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send("player-1", "message");
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, d => d.Id == "SGP100");
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public void Generator_reports_unsupported_event_property_type_as_diagnostic()
    {
        var compilation = CreateCompilation("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(decimal Amount);

            [Plugin("bad-event")]
            public sealed partial class BadKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send("player-1", "message");
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, d => d.Id == "SGP100");
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public void Generator_reports_handle_that_does_not_call_context_messages_send()
    {
        var compilation = CreateCompilation("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("bad-handle")]
            public sealed partial class BadKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => Send(e.TargetId, "message");

                private static void Send(string targetId, string message) { }
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics);

        Assert.Contains(generatorDiagnostics, d => d.Id == "SGP100");
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public void Generator_uses_handle_event_parameter_name_for_message_arguments()
    {
        var compilation = CreateCompilation("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("different-handle-name")]
            public sealed partial class GoodKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent evt, HookContext ctx)
                    => ctx.Messages.Send(evt.TargetId, "message");
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generated = Assert.Single(driver.GetRunResult().GeneratedTrees).GetText().ToString();
        Assert.Contains("Var(\"e_TargetId\")", generated);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
    }
}
