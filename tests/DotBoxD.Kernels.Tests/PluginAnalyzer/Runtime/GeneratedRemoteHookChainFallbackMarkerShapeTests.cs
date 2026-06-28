using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
{
    [Fact]
    public void Prebuilt_registry_marker_with_wrong_on_return_shape_is_ignored()
    {
        var sdk = CompileReference(WrongOnShapeMarkerSdkSource, "DotBoxDWrongOnShapeRegistryMarkerSdk");
        var result = RunGenerator(WrongOnShapeMarkerUsageSource, sdk);

        AssertNoHookChain(result);
    }

    [Fact]
    public void Generated_registry_marker_attribute_is_not_inheritable()
    {
        var usage = typeof(GeneratedPluginServerRegistryAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.False(usage.Inherited);
    }

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
    public void Same_compilation_generated_registry_method_return_type_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class MethodAliasUsage
            {
                private static AlphaPluginHookRegistry Hooks(AlphaPluginServer server)
                    => server.Hooks;

                public static void Configure(AlphaPluginServer server)
                    => Hooks(server).On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "method-alias"));
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("method-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_indexer_return_type_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public sealed class IndexerAliasUsage
            {
                private readonly AlphaPluginServer _server;

                public IndexerAliasUsage(AlphaPluginServer server)
                    => _server = server;

                private AlphaPluginHookRegistry this[int _]
                    => _server.Hooks;

                public void Configure()
                    => this[0].On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "indexer-alias"));
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("indexer-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_cast_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class CastAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    object hooks = server.Hooks;
                    ((AlphaPluginHookRegistry)hooks)
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "cast-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("cast-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_as_cast_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class AsCastAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    object hooks = server.Hooks;
                    (hooks as AlphaPluginHookRegistry)!
                        .On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "as-cast-alias"));
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("as-cast-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_pattern_alias_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class PatternAliasUsage
            {
                public static void Configure(AlphaPluginServer server)
                {
                    object hooks = server.Hooks;
                    if (hooks is AlphaPluginHookRegistry typed)
                    {
                        typed.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                            .Where(e => e.Distance <= 5)
                            .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "pattern-alias"));
                    }
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("pattern-alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_compilation_generated_registry_out_alias_lowers()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class OutAliasUsage
            {
                private static bool TryHooks(AlphaPluginServer server, out AlphaPluginHookRegistry hooks)
                {
                    hooks = server.Hooks;
                    return true;
                }

                public static void Configure(AlphaPluginServer server)
                {
                    if (TryHooks(server, out AlphaPluginHookRegistry hooks))
                    {
                        hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                            .Where(e => e.Distance <= 5)
                            .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "out-alias"));
                    }
                }
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("out-alias", generated, StringComparison.Ordinal);
    }

    private const string WrongOnShapeMarkerSdkSource = """
        using DotBoxD.Abstractions;

        namespace WrongShapeSdk;

        public sealed class WrongShapeContext
        {
            public WrongShapeContext(HookContext raw) => Raw = raw;
            public HookContext Raw { get; }
            public IPluginMessageSink Messages => Raw.Messages;
        }

        [GeneratePluginServer(Context = typeof(WrongShapeContext))]
        public sealed class WrongShapeServer
        {
            public WrongShapeHookRegistry Hooks
                => throw new System.InvalidOperationException("not used");
        }

        [GeneratedPluginServerRegistry(
            GeneratedPluginServerRegistryKind.Hook,
            typeof(WrongShapeServer),
            typeof(WrongShapeContext))]
        public sealed class WrongShapeHookRegistry
        {
            public WrongShapeHookPipeline<TEvent> On<TEvent>() => new();
        }

        public sealed class WrongShapeHookPipeline<TEvent>
        {
            public WrongShapeHookStage<TEvent> Where(global::System.Func<TEvent, bool> predicate) => new();
        }

        public sealed class WrongShapeHookStage<TEvent>
        {
            public void Run(global::System.Action<TEvent, WrongShapeContext> handler) { }
        }
        """;

    private const string WrongOnShapeMarkerUsageSource = """
        namespace ChainSample.Plugin;

        public static class RemoteServerUsage
        {
            public static void Configure(global::WrongShapeSdk.WrongShapeServer server)
            {
                var hooks = server.Hooks;
                hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
        }
        """;

}
