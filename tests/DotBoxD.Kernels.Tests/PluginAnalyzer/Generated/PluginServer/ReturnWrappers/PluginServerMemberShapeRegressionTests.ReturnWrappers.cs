namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerMemberShapeRegressionTests
{
    [Fact]
    public void Generated_plugin_server_wraps_world_methods_returning_service_handles()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    ValueTask<IMonster> FindMonsterAsync(string id);
            """, """

                [RpcService]
                public interface IMonster
                {
                    string Id { get; }
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("private sealed class MonsterPluginService", generated, StringComparison.Ordinal);
        Assert.Contains(
            "public async global::System.Threading.Tasks.ValueTask<global::Regression.Game.IMonster> FindMonsterAsync(string @id) => new MonsterPluginService(this, await ((global::Regression.Game.IGameWorldAccess)RequireWorld()).FindMonsterAsync(@id).ConfigureAwait(false));",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_reports_world_service_wrapper_collisions()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [RpcService]
                public interface IGameWorldAccess
                {
                    ValueTask<IMonster> FindMonsterAsync(string id);
                }

                [RpcService]
                public interface IMonster;
            }

            namespace Regression.Game.Ipc
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
                    public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Regression.Plugin
            {
                using DotBoxD.Abstractions;
                using Regression.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess
                {
                    private sealed class MonsterPluginService;
                }

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("MonsterPluginService", StringComparison.Ordinal));
    }
}
