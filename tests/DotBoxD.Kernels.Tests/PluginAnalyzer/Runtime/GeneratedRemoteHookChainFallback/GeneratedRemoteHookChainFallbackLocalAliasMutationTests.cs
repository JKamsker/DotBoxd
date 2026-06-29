namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
{
    [Fact]
    public void Same_compilation_generated_registry_local_alias_reassignment_is_not_lowered()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class ReassignedLocalAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    var hooks = server.Hooks;
                    hooks = server.Hooks;
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "reassigned-local-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.DoesNotContain("reassigned-local-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_local_alias_nested_lambda_reassignment_still_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class NestedLambdaReassignedLocalAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    var hooks = server.Hooks;
                    System.Action reset = () => hooks = server.Hooks;
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "nested-lambda-reassigned-local-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("nested-lambda-reassigned-local-alias", generated, StringComparison.Ordinal);
    }
}
