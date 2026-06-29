namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
{
    [Fact]
    public void Same_compilation_generated_registry_positional_record_property_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public sealed record HookBag(AlphaPluginHookRegistry Hooks);

            public static class PositionalRecordPropertyUsage
            {
                public static void Configure(AlphaPluginServer server)
                    => new HookBag(server.Hooks)
                        .Hooks
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "positional-record-property"));
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("positional-record-property", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_result_property_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public sealed class ResultHookBag
            {
                public ResultHookBag(AlphaPluginHookRegistry result)
                    => Result = result;

                public AlphaPluginHookRegistry Result { get; }
            }

            public static class ResultPropertyUsage
            {
                public static void Configure(AlphaPluginServer server)
                    => new ResultHookBag(server.Hooks)
                        .Result
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "result-property"));
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("result-property", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_anonymous_object_property_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class AnonymousObjectPropertyUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    var bag = new { Hooks = server.Hooks };
                    bag.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "anonymous-object-property"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("anonymous-object-property", generated, StringComparison.Ordinal);
    }
}
