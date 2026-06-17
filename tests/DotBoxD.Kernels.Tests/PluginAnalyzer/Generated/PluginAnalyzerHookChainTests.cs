using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

/// <summary>
/// Phase C lowering: the generator lowers an inline On&lt;TEvent&gt;().Where(lambda).InvokeKernel(lambda)
/// chain into a verified-IR package — the lambda bodies become the module's ShouldHandle/Handle — and
/// fails safe (emits nothing, no DBXK100) for shapes outside the supported subset.
/// </summary>
public sealed class PluginAnalyzerHookChainTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Lowers_a_Where_then_InvokeKernel_chain_to_a_package()
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
                        .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_bare_InvokeKernel_chain_with_no_Where()
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
                        .InvokeKernel((e, ctx) => ctx.Messages.Send(e.AttackerId, "taunt"));
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
                        .InvokeKernel((e, ctx) => ctx.Messages.Send("clock", "tick"));
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
                        .InvokeKernel((gap, ctx) => ctx.Messages.Send("monster", "calm"));
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
                        .InvokeKernel((id, ctx) => ctx.Messages.Send(id, "calm"));
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
                        .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
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
                        .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
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
                        .InvokeKernel((gap, ctx) => ctx.Messages.Send("monster", "calm"));
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
                        .InvokeKernel((id, ctx) => ctx.Messages.Send(id, "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Does_not_lower_an_element_only_InvokeKernel_terminal()
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
                        .InvokeKernel(e => default);
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
                        .InvokeKernel((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            }
            """);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
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
