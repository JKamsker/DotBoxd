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
        Assert.Contains("Not(StringEquals(Var(\"e_Message\"), Str(\"\")))", generated);
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
    public void Generator_emits_direct_parameter_array_construction_without_linq()
    {
        var (result, outputCompilation, diagnostics) = RunGenerator("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            [GamePlugin("direct-parameters")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int MinDamage { get; set; } = 100;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("var parameters = Parameters(settings);", generated);
        Assert.Contains("parameters[i + 2] = new global::SafeIR.Parameter(setting.Name, TypeOf(setting.Type));", generated);
        Assert.DoesNotContain("global::System.Linq.Enumerable.", generated);
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

    [Fact]
    public void Generator_promotes_numeric_constants_to_supported_operand_type()
    {
        var (result, outputCompilation, diagnostics) = RunGenerator("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, long Sequence, double Ratio);

            [GamePlugin("promoted-numeric-constants")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Sequence + 1 > -1 && e.Ratio * 2 >= 1;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("I64(1L)", generated);
        Assert.Contains("I64(-1L)", generated);
        Assert.Contains("F64(2D)", generated);
        Assert.Contains("F64(1D)", generated);
    }

    [Fact]
    public void Generator_lowers_supported_constant_relational_and_not_patterns()
    {
        var (result, outputCompilation, diagnostics) = RunGenerator("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, long Sequence, double Ratio);

            [GamePlugin("patterns")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Message is "fire" && e.Sequence is > 0 && e.Ratio is not <= 1;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("StringEquals(Var(\"e_Message\"), Str(\"fire\"))", generated);
        Assert.Contains("Gt(Var(\"e_Sequence\"), I64(0L))", generated);
        Assert.Contains("Not(Le(Var(\"e_Ratio\"), F64(1D)))", generated);
    }

    [Fact]
    public void Generator_lowers_should_handle_conditional_expression_to_ordered_branches()
    {
        var (result, outputCompilation, diagnostics) = RunGenerator("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, int Amount, bool Enabled);

            [GamePlugin("conditional-expression")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Enabled ? e.Amount > 0 : e.Amount == 0;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.Contains("new global::SafeIR.IfStatement(Var(\"e_Enabled\")", generated);
        Assert.Contains("Gt(Var(\"e_Amount\"), I32(0))", generated);
        Assert.Contains("Eq(Var(\"e_Amount\"), I32(0))", generated);
    }

    [Theory]
    [InlineData("e.Missing == \"fire\"")]
    [InlineData("e.Amount > 0.5D")]
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
