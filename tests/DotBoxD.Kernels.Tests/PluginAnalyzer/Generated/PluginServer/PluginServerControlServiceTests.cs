namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerControlServiceTests
{
    [Fact]
    public void Generated_plugin_server_prefers_explicit_control_service_over_convention()
    {
        // Both the explicit ControlService and the {World}.Ipc.IGamePluginControlService convention type exist.
        // The explicit contract must win: the generated facade targets it (and its update type), never the
        // convention contract.
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Precedence.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace Precedence.Game.Ipc
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

            namespace Precedence.Control
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
                    public static Precedence.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Precedence.Plugin
            {
                using DotBoxD.Abstractions;
                using Precedence.Game;

                [GeneratePluginServer(
                    Context = typeof(RemotePluginContext),
                    ControlService = typeof(Precedence.Control.IPluginControl))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("global::Precedence.Control.IPluginControl", generated, StringComparison.Ordinal);
        Assert.Contains("global::Precedence.Control.PluginSettingPatch", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IGamePluginControlService", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveSettingUpdate", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_infers_update_type_from_array_parameter_regardless_of_name()
    {
        // UpdateSettingsAsync names its update batch `patches`, not `updates`. The update element type must still
        // be inferred from the array parameter so an explicit contract is not forced into a specific name.
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Renamed.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace Renamed.Control
            {
                public readonly record struct SettingPatch(string Name, string Value);

                public interface IPluginControl : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        SettingPatch[] patches,
                        bool atomic = false,
                        CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Renamed.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Renamed.Plugin
            {
                using DotBoxD.Abstractions;
                using Renamed.Game;

                [GeneratePluginServer(
                    Context = typeof(RemotePluginContext),
                    ControlService = typeof(Renamed.Control.IPluginControl))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("global::Renamed.Control.SettingPatch", generated, StringComparison.Ordinal);
    }
}
