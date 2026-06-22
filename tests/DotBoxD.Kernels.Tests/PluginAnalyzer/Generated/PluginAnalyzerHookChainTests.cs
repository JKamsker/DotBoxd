using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

/// <summary>
/// Phase C lowering: the generator lowers an inline On&lt;TEvent&gt;().Where(lambda).Run(lambda)
/// chain into a verified-IR package — the lambda bodies become the module's ShouldHandle/Handle — and
/// fails safe (emits nothing, no DBXK100) for shapes outside the supported subset.
/// </summary>
public sealed class PluginAnalyzerHookChainTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Lowers_a_Where_then_Run_chain_to_a_package()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Where((e, ctx) => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_bare_Run_chain_with_no_Where()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record AttackEvent(string AttackerId, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<AttackEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.AttackerId, "taunt"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_zero_property_event_chain()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record TickEvent;

            public static class Usage
            {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<TickEvent>()
                        .Run((e, ctx) => ctx.Messages.Send("clock", "tick"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_multi_Where_plus_Select_chain_substituting_the_projection()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance, int MonsterLevel, int PlayerLevel);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Where((e, ctx) => e.Distance <= 5)
                        .Select((e, ctx) => e.MonsterLevel - e.PlayerLevel)
                        .Where((gap, ctx) => gap >= 3)
                        .Run((gap, ctx) => ctx.Messages.Send("monster", "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_Select_projection_used_as_the_terminal_send_target()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Select((e, ctx) => e.MonsterId)
                        .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_simple_one_parameter_Where_chain()
    {
        // Where(e => ...) — element only, simple lambda (no parens), no context — lowers like (e, ctx) =>.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_parenthesized_single_parameter_Where_chain()
    {
        // Where((e) => ...) — parenthesized single parameter.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Where((e) => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_one_parameter_Select_and_Where_chain()
    {
        // Select(e => ...) and Where(gap => ...) — both element-only — lower with the projection.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance, int MonsterLevel, int PlayerLevel);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Select(e => e.MonsterLevel - e.PlayerLevel)
                        .Where(gap => gap >= 3)
                        .Run((gap, ctx) => ctx.Messages.Send("monster", "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_chain_mixing_one_and_two_parameter_stages_independently()
    {
        // Each stage independently chooses its arity: 1-param Where, 2-param Where, 1-param Select.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance, int MonsterLevel);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Where((e, ctx) => e.MonsterLevel >= 3)
                        .Select(e => e.MonsterId)
                        .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Does_not_lower_an_element_only_Run_terminal()
    {
        // An element-only terminal has no context, so it cannot ctx.Messages.Send — the only lowerable
        // terminal effect. It must fail safe: no HookChain_ package, leaving the runtime terminal to throw.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Where((e, ctx) => e.Distance <= 5)
                        .Run(e => default);
            }
            """);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Does_not_lower_hook_chain_with_unsupported_event_property_type()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MixedEvent(string TargetId, DateTime When);

            public static class Usage
            {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<MixedEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            }
            """);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Interceptor_attribute_coexists_with_existing_definition()
    {
        var output = RunGeneratorCompilation("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace System.Runtime.CompilerServices
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
                internal sealed class InterceptsLocationAttribute : global::System.Attribute
                {
                    public InterceptsLocationAttribute(int version, string data) { }
                }
            }

            namespace Sample
            {

                public sealed record ExistingAttributeEvent(string TargetId);

                public static class Usage
                {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<ExistingAttributeEvent>()
                            .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        Assert.DoesNotContain(
            output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Id is "CS0101" or "CS0111");
    }

    [Fact]
    public void Lowers_a_Register_result_chain_to_a_result_install()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public enum Relation { Pve = 0, Pvp = 1 }

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(Relation Relation, int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Relation == Relation.Pve)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage * 2 }, 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Result_hook_manifest_uses_the_declared_hook_name()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("\"combat.damage\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Sample.DamageCtx\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Lowers_a_RegisterLocal_result_chain_to_a_local_result_install()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.death", typeof(DeathResult))]
            public sealed record DeathCtx(int FatalDamage);

            [HookResult]
            public readonly partial record struct DeathResult(bool Success, string? Reason, int Mitigated);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DeathCtx>()
                        .Where(ctx => ctx.FatalDamage > 0)
                        .RegisterLocal((ctx, hookContext) => new DeathResult { Success = true }, 5);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedLocalResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_remote_Register_result_chain_to_a_remote_result_install()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Damage > 0)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 25);
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("UseGeneratedResultChain<global::Sample.DamageResult>", generated, StringComparison.Ordinal);
        Assert.Contains("ResultType = \"global::Sample.DamageResult\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("                LocalTerminal = true", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Lowers_a_remote_RegisterLocal_result_chain_to_a_remote_local_result_install()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Damage > 0)
                        .RegisterLocal((ctx, hookContext) => new DamageResult { Success = true, Damage = ctx.Damage }, 25);
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("UseGeneratedLocalResultChain<global::Sample.DamageResult>", generated, StringComparison.Ordinal);
        Assert.Contains("ResultType = \"global::Sample.DamageResult\"", generated, StringComparison.Ordinal);
        Assert.Contains("ResultLocalTerminal = true", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("                LocalTerminal = true", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Lowers_a_cancellation_aware_RegisterLocal_result_chain_to_a_local_result_install()
    {
        var result = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.death", typeof(DeathResult))]
            public sealed record DeathCtx(int FatalDamage);

            [HookResult]
            public readonly partial record struct DeathResult(bool Success, string? Reason, int Mitigated);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DeathCtx>()
                        .Where(ctx => ctx.FatalDamage > 0)
                        .RegisterLocal(
                            (ctx, hookContext, cancellationToken) =>
                                new ValueTask<DeathResult>(new DeathResult { Success = true }),
                            5);
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("UseGeneratedLocalResultChain", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Threading.CancellationToken", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Lowers_a_Register_fluent_builder_chain_to_a_result_install()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage * 2), 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Register_with_author_defined_result_builder_member_reports_DBXK113()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage)
            {
                public static DamageResult Ok() => new() { Success = true, Damage = 999 };
            }

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage), 100);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Register_with_author_defined_with_member_reports_DBXK113()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage)
            {
                public DamageResult WithDamage(int damage) => this with { Damage = 999 };
            }

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage), 100);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void RegisterLocal_returning_the_wrong_result_type_reports_DBXK113()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            [HookResult]
            public readonly partial record struct OtherResult(bool Success, string? Reason);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .RegisterLocal((ctx, hookContext) => new OtherResult { Success = true }, 0);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Register_on_a_context_without_Hook_reports_DBXK113()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record NoHookCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<NoHookCtx>()
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Unlowered_Register_reports_DBXK113_as_a_warning()
    {
        // A sandbox Register that fails to lower has no in-process fallback (it always throws at first dispatch),
        // so it is raised to Warning so the author sees it rather than the default-suppressed Info.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record NoHookCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<NoHookCtx>()
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK113"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void Unlowered_RegisterLocal_reports_DBXK113_as_info()
    {
        // RegisterLocal is an escape hatch whose body need not lower; a not-lowered case stays Info, consistent
        // with the remote RunLocal (DBXK111) not-lowered diagnostic.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record NoHookCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<NoHookCtx>()
                        .RegisterLocal((ctx, hookContext) => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK113"));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
        => RunGeneratorCore(source).Result;

    private static Compilation RunGeneratorCompilation(string source)
        => RunGeneratorCore(source).Output;

    private static (Compilation Output, GeneratorDriverRunResult Result) RunGeneratorCore(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginServer).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (output, driver.GetRunResult());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
