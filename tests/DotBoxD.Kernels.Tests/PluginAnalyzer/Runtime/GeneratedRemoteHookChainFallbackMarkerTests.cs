using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
{
    [Fact]
    public void Prebuilt_registry_marker_without_generated_server_ownership_is_ignored()
    {
        var sdk = CompileReference(UnownedMarkerSdkSource, "DotBoxDUnownedRegistryMarkerSdk");
        var result = RunGenerator(UnownedMarkerUsageSource, sdk);

        AssertNoHookChain(result);
    }

    [Fact]
    public void Prebuilt_registry_marker_with_context_mismatch_is_ignored()
    {
        var sdk = CompileReference(ContextMismatchMarkerSdkSource, "DotBoxDContextMismatchRegistryMarkerSdk");
        var result = RunGenerator(ContextMismatchMarkerUsageSource, sdk);

        AssertNoHookChain(result);
    }

    [Fact]
    public void Prebuilt_registry_marker_lookalike_attributes_are_ignored()
    {
        var sdk = CompileReference(LookalikeMarkerSdkSource, "DotBoxDLookalikeRegistryMarkerSdk");
        var result = RunGenerator(LookalikeMarkerUsageSource, sdk);

        AssertNoHookChain(result);
    }

    private static void AssertNoHookChain(GeneratorDriverRunResult result)
    {
        Assert.DoesNotContain(GeneratedSources(result), source => source.Contains("HookChain_", StringComparison.Ordinal));
        Assert.DoesNotContain(GeneratedSources(result), source =>
            source.Contains("HookChainInterceptors", StringComparison.Ordinal));
    }

    private const string UnownedMarkerSdkSource = """
        using DotBoxD.Abstractions;

        namespace FakeSdk;

        public sealed class FakeContext
        {
            public FakeContext(HookContext raw) => Raw = raw;
            public HookContext Raw { get; }
            public IPluginMessageSink Messages => Raw.Messages;
        }

        public sealed class FakeServer
        {
            public FakeHookRegistry Hooks
                => throw new System.InvalidOperationException("not used");
        }

        [GeneratedPluginServerRegistry(
            GeneratedPluginServerRegistryKind.Hook,
            typeof(FakeServer),
            typeof(FakeContext))]
        public sealed class FakeHookRegistry
        {
            public FakeHookPipeline<TEvent> On<TEvent>() => new();
        }

        public sealed class FakeHookPipeline<TEvent>
        {
            public FakeHookStage<TEvent> Where(global::System.Func<TEvent, bool> predicate) => new();
        }

        public sealed class FakeHookStage<TEvent>
        {
            public void Run(global::System.Action<TEvent, FakeContext> handler) { }
        }
        """;

    private const string UnownedMarkerUsageSource = """
        namespace ChainSample.Plugin;

        public static class RemoteServerUsage
        {
            public static void Configure(global::FakeSdk.FakeServer server)
            {
                var hooks = server.Hooks;
                hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
        }
        """;

    private const string ContextMismatchMarkerSdkSource = """
        using DotBoxD.Abstractions;

        namespace MismatchSdk;

        public sealed class RealContext
        {
            public RealContext(HookContext raw) => Raw = raw;
            public HookContext Raw { get; }
            public IPluginMessageSink Messages => Raw.Messages;
        }

        public sealed class OtherContext
        {
            public OtherContext(HookContext raw) => Raw = raw;
            public HookContext Raw { get; }
            public IPluginMessageSink Messages => Raw.Messages;
        }

        [GeneratePluginServer(Context = typeof(RealContext))]
        public sealed class MismatchServer
        {
            public MismatchHookRegistry Hooks
                => throw new System.InvalidOperationException("not used");
        }

        [GeneratedPluginServerRegistry(
            GeneratedPluginServerRegistryKind.Hook,
            typeof(MismatchServer),
            typeof(OtherContext))]
        public sealed class MismatchHookRegistry
        {
            public MismatchHookPipeline<TEvent> On<TEvent>() => new();
        }

        public sealed class MismatchHookPipeline<TEvent>
        {
            public MismatchHookStage<TEvent> Where(global::System.Func<TEvent, bool> predicate) => new();
        }

        public sealed class MismatchHookStage<TEvent>
        {
            public void Run(global::System.Action<TEvent, OtherContext> handler) { }
        }
        """;

    private const string ContextMismatchMarkerUsageSource = """
        namespace ChainSample.Plugin;

        public static class RemoteServerUsage
        {
            public static void Configure(global::MismatchSdk.MismatchServer server)
            {
                var hooks = server.Hooks;
                hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
        }
        """;

    private const string LookalikeMarkerSdkSource = """
        namespace DotBoxD.Abstractions
        {
            [global::System.AttributeUsage(global::System.AttributeTargets.Class, Inherited = false)]
            public sealed class GeneratePluginServerAttribute : global::System.Attribute
            {
                public global::System.Type? Context { get; set; }
            }

            [global::System.AttributeUsage(global::System.AttributeTargets.Class, Inherited = false)]
            public sealed class GeneratedPluginServerRegistryAttribute(
                GeneratedPluginServerRegistryKind kind,
                global::System.Type serverType,
                global::System.Type contextType)
                : global::System.Attribute;

            public enum GeneratedPluginServerRegistryKind
            {
                Hook,
                Subscription,
            }
        }

        namespace LookalikeSdk
        {
            using DotBoxD.Abstractions;

            public sealed class LookalikeContext
            {
                public LookalikeContext(HookContext raw) => Raw = raw;
                public HookContext Raw { get; }
                public IPluginMessageSink Messages => Raw.Messages;
            }

            [GeneratePluginServer(Context = typeof(LookalikeContext))]
            public sealed class LookalikeServer
            {
                public LookalikeHookRegistry Hooks
                    => throw new System.InvalidOperationException("not used");
            }

            [GeneratedPluginServerRegistry(
                GeneratedPluginServerRegistryKind.Hook,
                typeof(LookalikeServer),
                typeof(LookalikeContext))]
            public sealed class LookalikeHookRegistry
            {
                public LookalikeHookPipeline<TEvent> On<TEvent>() => new();
            }

            public sealed class LookalikeHookPipeline<TEvent>
            {
                public LookalikeHookStage<TEvent> Where(global::System.Func<TEvent, bool> predicate) => new();
            }

            public sealed class LookalikeHookStage<TEvent>
            {
                public void Run(global::System.Action<TEvent, LookalikeContext> handler) { }
            }
        }
        """;

    private const string LookalikeMarkerUsageSource = """
        namespace ChainSample.Plugin;

        public static class RemoteServerUsage
        {
            public static void Configure(global::LookalikeSdk.LookalikeServer server)
            {
                var hooks = server.Hooks;
                hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
        }
        """;

}
