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
    public void Same_compilation_generated_registry_coalesce_assignment_expression_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class CoalesceAssignmentExpressionAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    AlphaPluginHookRegistry? hooks = null;
                    (hooks ??= server.Hooks)
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "coalesce-assignment-expression-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("coalesce-assignment-expression-alias", generated, StringComparison.Ordinal);
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

    [Fact]
    public void Same_compilation_generated_registry_awaited_receiver_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class AwaitedReceiverUsage
            {
                private static System.Threading.Tasks.Task<AlphaPluginHookRegistry> HooksAsync(
                    AlphaPluginServer server)
                    => System.Threading.Tasks.Task.FromResult(server.Hooks);

                public static async System.Threading.Tasks.Task Configure(AlphaPluginServer server)
                {
                    (await HooksAsync(server))
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "awaited-receiver"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("awaited-receiver", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_awaited_var_receiver_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class AwaitedVarReceiverUsage
            {
                private static System.Threading.Tasks.Task<AlphaPluginHookRegistry> HooksAsync(
                    AlphaPluginServer server)
                    => System.Threading.Tasks.Task.FromResult(server.Hooks);

                public static async System.Threading.Tasks.Task Configure(AlphaPluginServer server)
                {
                    var hooksTask = HooksAsync(server);
                    (await hooksTask)
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "awaited-var-receiver"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("awaited-var-receiver", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_task_result_receiver_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class TaskResultReceiverUsage
            {
                private static System.Threading.Tasks.Task<AlphaPluginHookRegistry> HooksAsync(
                    AlphaPluginServer server)
                    => System.Threading.Tasks.Task.FromResult(server.Hooks);

                public static void Configure(AlphaPluginServer server)
                {
                    HooksAsync(server)
                        .Result
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "task-result-receiver"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("task-result-receiver", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_task_result_var_receiver_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class TaskResultVarReceiverUsage
            {
                private static System.Threading.Tasks.Task<AlphaPluginHookRegistry> HooksAsync(
                    AlphaPluginServer server)
                    => System.Threading.Tasks.Task.FromResult(server.Hooks);

                public static void Configure(AlphaPluginServer server)
                {
                    var hooksTask = HooksAsync(server);
                    hooksTask
                        .Result
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "task-result-var-receiver"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("task-result-var-receiver", generated, StringComparison.Ordinal);
    }

}
