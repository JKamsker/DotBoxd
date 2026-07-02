namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerRemoteLocalInheritanceTests
{
    [Fact]
    public void Generated_plugin_server_wires_event_callback_methods_inherited_from_base_interface()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Reactive.Game
            {
                [RpcService]
                public interface IGameWorldAccess;
            }

            namespace Reactive.Game.Ipc
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

                public interface IPluginEventCallbackBase
                {
                    ValueTask OnEventAsync(
                        string subscriptionId,
                        System.ReadOnlyMemory<byte> projectedValue,
                        CancellationToken ct = default);
                    ValueTask<byte[]> OnResultAsync(
                        string subscriptionId,
                        System.ReadOnlyMemory<byte> contextValue,
                        CancellationToken ct = default);
                }

                [RpcService]
                public interface IPluginEventCallback : IPluginEventCallbackBase;
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Reactive.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");

                    public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                        DotBoxD.Services.Peer.RpcPeer peer,
                        Reactive.Game.Ipc.IPluginEventCallback implementation)
                        => peer;
                }
            }

            namespace Reactive.Plugin
            {
                using DotBoxD.Abstractions;
                using Reactive.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains(
            "private readonly global::DotBoxD.Plugins.Runtime.Hooks.RemoteLocalHandlerRegistry _localHandlers = new();",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.ProvidePluginEventCallback(peer, new RemoteLocalEventSink(_localHandlers))",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "private sealed class RemoteLocalEventSink : global::Reactive.Game.Ipc.IPluginEventCallback",
            generated,
            StringComparison.Ordinal);
    }
}
