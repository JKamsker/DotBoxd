namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
{
    [Fact]
    public void Same_compilation_generated_registry_deconstruction_alias_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class DeconstructionAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    var (hooks, _) = (server.Hooks, 0);
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "deconstruction-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("deconstruction-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_nested_deconstruction_alias_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class NestedDeconstructionAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    var ((hooks, _), _) = ((server.Hooks, 0), "ignored");
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "nested-deconstruction-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("nested-deconstruction-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_assignment_expression_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class AssignmentExpressionAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    AlphaPluginHookRegistry hooks = null!;
                    (hooks = server.Hooks)
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "assignment-expression-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("assignment-expression-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_tuple_return_deconstruction_alias_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class TupleReturnDeconstructionAliasUsage
            {
                private static (AlphaPluginHookRegistry Hooks, int Ignored) Pair(AlphaPluginServer server)
                    => (server.Hooks, 0);

                public static void Configure(AlphaPluginServer server)
                {
                    var (hooks, _) = Pair(server);
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "tuple-return-deconstruction-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("tuple-return-deconstruction-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_tuple_element_alias_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class TupleElementAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    var pair = (Hooks: server.Hooks, Ignored: 0);
                    pair.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "tuple-element-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("tuple-element-alias", generated, StringComparison.Ordinal);
    }
}
