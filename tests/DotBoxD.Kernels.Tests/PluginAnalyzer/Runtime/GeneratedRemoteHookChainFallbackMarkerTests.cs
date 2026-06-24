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

    private static void AssertNoHookChain(GeneratorDriverRunResult result)
    {
        Assert.DoesNotContain(GeneratedSources(result), source => source.Contains("HookChain_", StringComparison.Ordinal));
        Assert.DoesNotContain(GeneratedSources(result), source =>
            source.Contains("HookChainInterceptors", StringComparison.Ordinal));
    }

    private const string UnownedMarkerSdkSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins.Runtime;

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
            public RemoteHookPipeline<TEvent, FakeContext> On<TEvent>()
                => throw new System.InvalidOperationException("not used");
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
        using DotBoxD.Plugins.Runtime;

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
            public RemoteHookPipeline<TEvent, OtherContext> On<TEvent>()
                => throw new System.InvalidOperationException("not used");
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
}
