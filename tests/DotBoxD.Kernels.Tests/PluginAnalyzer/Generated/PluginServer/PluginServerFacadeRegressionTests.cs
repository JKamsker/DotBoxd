namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerFacadeRegressionTests
{
    [Fact]
    public void Generated_plugin_server_rejects_multiple_direct_world_interfaces()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [DotBoxDService]
                public interface IAlphaWorld;

                [DotBoxDService]
                public interface IBetaWorld;
            }

            namespace Regression.Plugin
            {
                using DotBoxD.Abstractions;
                using Regression.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IAlphaWorld, IBetaWorld;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains(
            "must directly implement one [DotBoxDService] world interface",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_includes_inherited_controls_and_wraps_async_handles()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [DotBoxDService]
                public interface IGameWorldBase
                {
                    IMonsterControl Monsters { get; }
                }

                [DotBoxDService]
                public interface IGameWorldAccess : IGameWorldBase;

                [DotBoxDService]
                public interface IMonsterControl
                {
                    ValueTask<IMonster> GetAsync(string entityId);
                }

                [DotBoxDService]
                public interface IMonster
                {
                    string Id { get; }
                    ValueTask<int> GetHealthAsync();
                }
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
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("public partial class RemotePluginContext", generated, StringComparison.Ordinal);
        Assert.Contains("RemotePluginHookRegistry Hooks { get; }", generated, StringComparison.Ordinal);
        Assert.Contains("RemotePluginSubscriptionRegistry Subscriptions { get; }", generated, StringComparison.Ordinal);
        Assert.Contains("public global::Regression.Game.IMonsterControl Monsters", generated, StringComparison.Ordinal);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask<global::Regression.Game.IMonster> GetAsync", generated, StringComparison.Ordinal);
        Assert.Contains("new MonsterPluginService(_owner, await _inner.GetAsync", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_accepts_explicit_control_service_and_infers_update_type()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Explicit.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace Explicit.Control
            {
                public readonly record struct PluginSettingPatch(string Name, string Value);

                public interface IPluginControl : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        PluginSettingPatch[] updates,
                        bool atomic = false,
                        CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Explicit.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Explicit.Plugin
            {
                using DotBoxD.Abstractions;
                using Explicit.Game;

                [GeneratePluginServer(
                    Context = typeof(RemotePluginContext),
                    ControlService = typeof(Explicit.Control.IPluginControl))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("global::Explicit.Control.IPluginControl", generated, StringComparison.Ordinal);
        Assert.Contains("global::Explicit.Control.PluginSettingPatch", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IGamePluginControlService", generated, StringComparison.Ordinal);
    }
}
