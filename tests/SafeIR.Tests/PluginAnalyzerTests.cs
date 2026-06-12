using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SafeIR;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_unsupported_live_setting_type()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                [LiveSetting]
                public decimal Anything { get; set; } = 1m;

                public bool ShouldHandle(string e, HookContext context) => true;

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP020");
    }

    [Fact]
    public async Task Reports_file_io_inside_event_kernel()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    System.IO.File.WriteAllText("x.txt", "bad");
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    [Theory]
    [InlineData("new System.Net.Http.HttpClient();")]
    [InlineData("System.Diagnostics.Process.Start(\"cmd.exe\");")]
    [InlineData("System.Threading.Tasks.Task.Run(() => { });")]
    [InlineData("System.Threading.Thread.Sleep(1);")]
    [InlineData("System.Environment.GetEnvironmentVariable(\"SECRET\");")]
    [InlineData("((System.IServiceProvider)null!).GetService(typeof(string));")]
    [InlineData("System.IO.Stream.Synchronized(null!);")]
    [InlineData("System.Reflection.Assembly.Load(\"System.Private.CoreLib\");")]
    [InlineData("var t = typeof(string);")]
    public async Task Reports_forbidden_host_apis_inside_event_kernel(string statement)
    {
        var diagnostics = await AnalyzeAsync($$"""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    {{statement}}
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    [Fact]
    public async Task Reports_forbidden_host_api_hidden_behind_helper_call()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            public static class BadHelper
            {
                public static void Write() => System.IO.File.WriteAllText("x.txt", "bad");
            }

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    BadHelper.Write();
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    [Fact]
    public void Generates_fire_damage_plugin_package_from_kernel_class()
    {
        var compilation = CreateCompilation("""
            using System.ComponentModel.DataAnnotations;
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string DamageType, int Amount, string TargetId);

            [GamePlugin("fire-damage")]
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
        Assert.Contains("And(Eq(Var(\"e_DamageType\"), Var(\"DamageType\")), Ge(Var(\"e_Amount\"), Var(\"MinDamage\")))", generated);
        Assert.Contains("global::SafeIR.Plugins.PluginMessageBindings.SendBindingId", generated);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
    }

    [Fact]
    public void Generator_reports_unsupported_kernel_shape_as_diagnostic()
    {
        var compilation = CreateCompilation("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string DamageType);

            [GamePlugin("bad-shape")]
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

            namespace Sample;

            public sealed record DamageEvent(string DamageType);

            [GamePlugin("bad-block")]
            public sealed partial class BadKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                {
                    if (e.DamageType == "ice")
                    {
                        return false;
                    }

                    return true;
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

            namespace Sample;

            public sealed record DamageEvent(decimal Amount);

            [GamePlugin("bad-event")]
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

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [GamePlugin("bad-handle")]
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

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [GamePlugin("different-handle-name")]
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

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SafeIrPluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            ParseOptions);
        return CSharpCompilation.Create(
            "SafeIrPluginAnalyzerTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GamePluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
