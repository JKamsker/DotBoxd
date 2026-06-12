using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerCompletenessTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generator_lowers_supported_bool_int_and_string_expression_subset()
    {
        var (result, outputCompilation, diagnostics) = RunGenerator("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, int Amount);

            [GamePlugin("complete-expression")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public bool Disabled { get; set; } = false;

                [LiveSetting]
                public int Offset { get; set; } = -1;

                [LiveSetting]
                public int MinDamage { get; set; } = 100;

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => !this.Disabled &&
                       (e.Amount + Offset >= MinDamage - 1) &&
                       e.Message != "";

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("new global::SafeIR.IfStatement(Var(\"Disabled\")", generated);
        Assert.Contains("Add(Var(\"e_Amount\"), Var(\"Offset\"))", generated);
        Assert.Contains("Sub(Var(\"MinDamage\"), I32(1))", generated);
        Assert.Contains("Ne(Var(\"e_Message\"), Str(\"\"))", generated);
        Assert.Contains("[\"Cpu\", \"Alloc\", \"GameStateWrite\", \"Audit\"]", generated);
    }

    [Fact]
    public void Generator_omits_alloc_effect_when_lowered_ir_has_no_allocating_literals()
    {
        var (result, outputCompilation, diagnostics) = RunGenerator("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            [GamePlugin("no-alloc")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("[\"Cpu\", \"GameStateWrite\", \"Audit\"]", generated);
        Assert.DoesNotContain("\"Alloc\"", generated);
    }

    [Fact]
    public void Generator_lowers_i64_and_f64_equality()
    {
        var (result, outputCompilation, diagnostics) = RunGenerator("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, long Sequence, double Ratio);

            [GamePlugin("wide-equality")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Sequence == 5L && e.Ratio == 1.5D;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("I64(5L)", generated);
        Assert.Contains("F64(1.5D)", generated);
    }

    [Theory]
    [InlineData("e.Missing == \"fire\"")]
    [InlineData("e.Amount > 0")]
    public void Generator_rejects_csharp_that_cannot_lower_to_valid_ir(string shouldHandleExpression)
    {
        var (result, _, diagnostics) = RunGenerator($$"""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, long Amount);

            [GamePlugin("invalid-lowering")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => {{shouldHandleExpression}};

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_rejects_handle_blocks_that_do_more_than_one_send()
    {
        var (result, _, diagnostics) = RunGenerator("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            [GamePlugin("invalid-handle")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                {
                    ctx.Messages.Send(e.TargetId, e.Message);
                    ctx.Messages.Send(e.TargetId, e.Message);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    private static (GeneratorDriverRunResult Result, Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics)
        RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);
        return (driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "SafeIrPluginCompletenessTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GamePluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

}
