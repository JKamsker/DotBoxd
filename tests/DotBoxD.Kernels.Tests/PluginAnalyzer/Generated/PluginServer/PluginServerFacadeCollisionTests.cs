namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerFacadeCollisionTests
{
    [Fact]
    public void Generated_plugin_server_disambiguates_same_simple_name_returned_services()
    {
        // Two returned services with the same simple name must get distinct wrapper class names.
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Collision.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    IMonsterControl Monsters { get; }
                }

                [DotBoxDService]
                public interface IMonsterControl
                {
                    ValueTask<Collision.Game.Alpha.IMonster> GetAlphaAsync(string id);
                    ValueTask<Collision.Game.Beta.IMonster> GetBetaAsync(string id);
                }
            }

            namespace Collision.Game.Alpha
            {
                [DotBoxDService]
                public interface IMonster
                {
                    ValueTask<int> GetHealthAsync();
                }
            }

            namespace Collision.Game.Beta
            {
                [DotBoxDService]
                public interface IMonster
                {
                    ValueTask<int> GetLevelAsync();
                }
            }

            namespace Collision.Game.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, System.Threading.CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, System.Threading.CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, System.Threading.CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        System.Threading.CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(System.Threading.CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Collision.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Collision.Plugin
            {
                using DotBoxD.Abstractions;
                using Collision.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("class MonsterPluginService :", generated, StringComparison.Ordinal);
        Assert.Contains("class MonsterPluginService_2 :", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_uses_disambiguated_world_proxy_suffix()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Collision.One
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace Collision.Two
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace Collision.Two.Ipc
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
                    public static Collision.Two.IGameWorldAccess GetCollision_Two_GameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Collision.Plugin
            {
                using DotBoxD.Abstractions;
                using Collision.Two;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("DotBoxDGeneratedExtensions.GetCollision_Two_GameWorldAccess", generated, StringComparison.Ordinal);
    }
}
