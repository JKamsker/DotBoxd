namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
{
    private const string GeneratedServerSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Services.Attributes;

        namespace ChainSample.Game
        {
            [RpcService]
            public interface IAlphaWorld;

            [RpcService]
            public interface IBetaWorld;
        }

        namespace ChainSample.Game.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static ChainSample.Game.IAlphaWorld GetAlphaWorld(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");

                public static ChainSample.Game.IBetaWorld GetBetaWorld(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }

        namespace ChainSample.Plugin
        {
            [Hook("chain.damage", typeof(ChainDamageResult))]
            public sealed record ChainDamageContext(int Damage);

            [HookResult]
            public readonly partial record struct ChainDamageResult(bool Success, string? Reason, int Damage);

            [GeneratePluginServer(Context = typeof(AlphaPluginContext))]
            public partial class AlphaPluginServer : ChainSample.Game.IAlphaWorld;

            public sealed partial class AlphaPluginContext;

            [GeneratePluginServer(Context = typeof(BetaPluginContext))]
            public partial class BetaPluginServer : ChainSample.Game.IBetaWorld;

            public sealed partial class BetaPluginContext;

            public static class RemoteServerUsage
            {
                public static void Configure(AlphaPluginServer alpha, BetaPluginServer beta)
                {
                    var hooks = alpha.Hooks;
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));

                    hooks.On<ChainDamageContext>()
                        .Where(e => e.Damage > 10)
                        .Register(e => new ChainDamageResult { Success = true, Damage = e.Damage }, 5);

                    var subscriptions = beta.Subscriptions;
                    subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "notify"));
                }
            }
        }
        """;

    private const string PrebuiltSdkSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins.Runtime;

        namespace SdkSample;

        public sealed class SdkContext
        {
            public SdkContext(HookContext raw) => Raw = raw;
            public HookContext Raw { get; }
            public IPluginMessageSink Messages => Raw.Messages;
            public static SdkContext FromHookContext(HookContext raw) => new(raw);
        }

        [GeneratePluginServer(Context = typeof(SdkContext))]
        public sealed class SdkServer
        {
            public SdkHookRegistry Hooks
                => throw new System.InvalidOperationException("not used");
        }

        [GeneratedPluginServerRegistry(
            GeneratedPluginServerRegistryKind.Hook,
            typeof(SdkServer),
            typeof(SdkContext))]
        public sealed class SdkHookRegistry
        {
            public RemoteHookPipeline<TEvent, SdkContext> On<TEvent>()
                => throw new System.InvalidOperationException("not used");
        }

        """;

    private const string PrebuiltSdkUsageSource = """
        namespace ChainSample.Plugin;

        public static class RemoteServerUsage
        {
            public static void Configure(global::SdkSample.SdkServer server)
            {
                var hooks = server.Hooks;
                hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
        }
        """;

    private const string ForeignHookSurfaceSource = """
        namespace ChainSample.Plugin;

        public sealed class ForeignServer
        {
            public ForeignHookRegistry Hooks { get; } = new();
            public ForeignSubscriptionRegistry Subscriptions { get; } = new();
        }

        public sealed class ForeignHookRegistry
        {
            public ForeignHookPipeline<TEvent> On<TEvent>() => new();
        }

        public sealed class ForeignHookPipeline<TEvent>
        {
            public ForeignHookPipeline<TEvent> Where(global::System.Func<TEvent, bool> predicate) => this;
            public void Run(global::System.Action<TEvent> handler) { }
        }

        public sealed class ForeignSubscriptionRegistry
        {
            public ForeignSubscriptionPipeline<TEvent> On<TEvent>() => new();
        }

        public sealed class ForeignSubscriptionPipeline<TEvent>
        {
            public ForeignSubscriptionPipeline<TEvent> Where(global::System.Func<TEvent, bool> predicate) => this;
            public void Run(global::System.Action<TEvent> handler) { }
        }

        public static class RemoteServerUsage
        {
            public static void Direct(ForeignServer server)
            {
                server.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run(e => { });

                server.Subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run(e => { });
            }

            public static void Alias(ForeignServer server)
            {
                var hooks = server.Hooks;
                hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run(e => { });

                var subscriptions = server.Subscriptions;
                subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run(e => { });
            }
        }
        """;

    private const string SameSimpleNameForeignRegistrySource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Services.Attributes;

        namespace ChainSample.Game
        {
            [RpcService]
            public interface IAlphaWorld;
        }

        namespace ChainSample.Game.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static ChainSample.Game.IAlphaWorld GetAlphaWorld(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }

        namespace ChainSample.Plugin
        {
            [GeneratePluginServer(Context = typeof(AlphaPluginContext))]
            public partial class AlphaPluginServer : ChainSample.Game.IAlphaWorld;

            public sealed partial class AlphaPluginContext;
        }

        namespace Foreign
        {
            public sealed class AlphaPluginServerHookRegistry
            {
                public ForeignHookPipeline<TEvent> On<TEvent>() => new();
            }

            public sealed class ForeignHookPipeline<TEvent>
            {
                public ForeignHookPipeline<TEvent> Where(global::System.Func<TEvent, bool> predicate) => this;
                public void Run(global::System.Action<TEvent> handler) { }
            }
        }

        namespace Consumer
        {
            using Foreign;

            public static class Usage
            {
                public static void Configure()
                {
                    AlphaPluginServerHookRegistry hooks = new();
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run(e => { });
                }
            }
        }
        """;
}
