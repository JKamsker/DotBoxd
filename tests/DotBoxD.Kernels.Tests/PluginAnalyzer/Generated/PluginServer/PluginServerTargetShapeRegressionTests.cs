using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerTargetShapeRegressionTests
{
    [Theory]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext))]
        public class RemotePluginServer : Sample.Game.IGameWorld
        {
        }
        """,
        "must be partial")]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext))]
        public partial class RemotePluginServer<T> : Sample.Game.IGameWorld;
        """,
        "must be non-generic")]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext))]
        public abstract partial class RemotePluginServer : Sample.Game.IGameWorld;
        """,
        "must be concrete")]
    [InlineData(
        """
        public partial class Outer
        {
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;
        }
        """,
        "must be non-nested")]
    public void Invalid_plugin_server_target_shape_reports_generation_diagnostic(
        string serverSource,
        string expectedMessage)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer(serverSource));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains(expectedMessage, StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    private static string MinimalServer(string serverSource)
        => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Sample.Game
            {
                [DotBoxDService]
                public interface IGameWorld;
            }

            namespace Sample.Game.Ipc
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
                    public static Sample.Game.IGameWorld GetGameWorld(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Sample.Plugin
            {
                {{serverSource}}

                public sealed partial class GameContext;
            }
            """;
}
