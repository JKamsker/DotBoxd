using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncArrayRegressionTests
{
    [Fact]
    public void Jagged_array_return_generates_compilable_reader()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Kernels.Game.Plugin.Client;
            using DotBoxD.Kernels.Game.Server.Abstractions;

            namespace DotBoxD.Kernels.Game.Server.Abstractions
            {
                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    [HostBinding("host.world.getHealthGrid", "game.world.monster.read.healthGrid", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    int[][] GetHealthGrid(string entityId);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
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

            namespace DotBoxD.Kernels.Game.Plugin.Client
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }

            namespace Sample
            {
                public static class Usage
                {
                    public static ValueTask<int[][]> Run(RemotePluginServer kernels)
                        => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                        {
                            return world.GetHealthGrid("monster-1");
                        });
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }
}
