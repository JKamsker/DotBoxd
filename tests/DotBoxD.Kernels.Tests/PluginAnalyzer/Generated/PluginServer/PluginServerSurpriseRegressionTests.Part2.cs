using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Fact]
    public void Generated_plugin_server_reports_existing_server_interface_type_collision()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(BaseServerSource(extraPluginTypes: """

                public interface IGameWorldServer;
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("IGameWorldServer", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0101");
    }

    [Fact]
    public void Generated_plugin_server_reports_existing_builder_type_collision()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(BaseServerSource(extraPluginTypes: """

                public sealed class RemotePluginServerBuilder
                {
                }
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("RemotePluginServerBuilder", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0101");
    }

    [Fact]
    public void Generated_plugin_server_reports_existing_control_accumulator_type_collision()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(BaseServerSource(worldMembers: """
                    ITools Tools { get; }
            """, extraGameTypes: """

                [RpcService]
                public interface ITools;
            """, extraPluginTypes: """

                public interface ToolsAccumulator
                {
                }
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("ToolsAccumulator", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0101");
    }

    [Fact]
    public void Generated_plugin_server_reports_existing_control_wrapper_type_collision()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(BaseServerSource(worldMembers: """
                    ITools Tools { get; }
            """, serverMembers: """
                private sealed class ToolsPluginControl;
            """, extraGameTypes: """

                [RpcService]
                public interface ITools;
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("ToolsPluginControl", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0102");
    }

    [Fact]
    public void Generated_plugin_server_reports_private_generated_facade_field_collision()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(BaseServerSource(serverMembers: """
                private bool _started;
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("_started", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("collides with the generated facade surface", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0102");
    }

    [Fact]
    public void Generated_plugin_server_reports_existing_InvokeAsync_signature_collision()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(BaseServerSource(serverMembers: """
                public ValueTask<TReturn> InvokeAsync<TReturn>(
                    System.Func<IGameWorldAccess, ValueTask<TReturn>> lambda,
                    CancellationToken cancellationToken = default)
                    => default;
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("InvokeAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0111");
    }

    [Fact]
    public void Generated_plugin_server_reports_existing_RequireInstalledKernel_signature_collision()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(BaseServerSource(serverMembers: """
                private void RequireInstalledKernel<TKernel>(string pluginId)
                {
                }
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("RequireInstalledKernel", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("collides with the generated facade surface", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0111");
    }

    [Fact]
    public void Generated_plugin_server_reports_existing_RequireInstalledPackageId_signature_collision()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(BaseServerSource(serverMembers: """
                private static void RequireInstalledPackageId(PluginPackage package, string pluginId)
                {
                }
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("RequireInstalledPackageId", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("collides with the generated facade surface", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0111");
    }

    [Fact]
    public void Generated_plugin_server_rejects_live_setting_update_type_without_name_value_constructor()
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
                public interface IGameWorldAccess;
            }

            namespace Regression.Game.Ipc
            {
                public sealed class LiveSettingUpdate
                {
                    public LiveSettingUpdate()
                    {
                    }

                    public string Name { get; init; } = "";

                    public string Value { get; init; } = "";
                }

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

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("LiveSettingUpdate", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("constructor", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS1729");
    }

    [Fact]
    public void Generated_plugin_server_rejects_inaccessible_live_setting_update_type()
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
                public interface IGameWorldAccess;
            }

            namespace Regression.Game.Ipc
            {
                public sealed class LiveSettingEnvelope
                {
                    private readonly record struct LiveSettingUpdate(string Name, string Value);

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

                [GeneratePluginServer(
                    Context = typeof(RemotePluginContext),
                    ControlService = typeof(Regression.Game.Ipc.LiveSettingEnvelope.IGamePluginControlService))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("LiveSettingUpdate", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("accessible", StringComparison.Ordinal));
    }
}
