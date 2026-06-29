namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
{
    [Fact]
    public void Same_compilation_generated_registry_conditional_alias_lowers_when_branches_match()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class ConditionalAliasUsage
            {
                public static void Configure(
                    AlphaPluginServer primary,
                    AlphaPluginServer fallback,
                    bool usePrimary)
                {
                    var hooks = usePrimary ? primary.Hooks : fallback.Hooks;
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "conditional-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("conditional-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_coalesce_alias_lowers_when_operands_match()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class CoalesceAliasUsage
            {
                public static void Configure(
                    AlphaPluginServer primary,
                    AlphaPluginServer fallback)
                {
                    var hooks = primary.Hooks ?? fallback.Hooks;
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "coalesce-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("coalesce-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_conditional_access_coalesce_alias_lowers_when_operands_match()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class ConditionalAccessCoalesceAliasUsage
            {
                public static void Configure(
                    AlphaPluginServer? primary,
                    AlphaPluginServer fallback)
                {
                    var hooks = primary?.Hooks ?? fallback.Hooks;
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "conditional-access-coalesce-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("conditional-access-coalesce-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_switch_alias_lowers_when_arms_match()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class SwitchAliasUsage
            {
                public static void Configure(
                    AlphaPluginServer primary,
                    AlphaPluginServer fallback,
                    int shard)
                {
                    var hooks = shard switch
                    {
                        0 => primary.Hooks,
                        _ => fallback.Hooks,
                    };
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "switch-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("switch-alias", generated, StringComparison.Ordinal);
    }
}
