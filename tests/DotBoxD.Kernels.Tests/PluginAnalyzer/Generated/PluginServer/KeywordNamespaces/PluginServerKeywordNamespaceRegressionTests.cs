namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerKeywordNamespaceRegressionTests
{
    [Fact]
    public void Generated_plugin_server_resolves_convention_control_service_in_keyword_namespace()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(Source());

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("namespace Keyword.@event.Plugin", generated, StringComparison.Ordinal);
        Assert.Contains("global::Keyword.@event.Ipc.IGamePluginControlService", generated, StringComparison.Ordinal);
        Assert.Contains("RemotePluginServerBuilder", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_with_explicit_control_service_emits_source_in_keyword_namespace()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(Source(
            "ControlService = typeof(Keyword.@event.Ipc.IGamePluginControlService)"));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("namespace Keyword.@event.Plugin", generated, StringComparison.Ordinal);
        Assert.Contains("global::Keyword.@event.Ipc.IGamePluginControlService", generated, StringComparison.Ordinal);
        Assert.Contains("RemotePluginServerBuilder", generated, StringComparison.Ordinal);
    }

    private static string Source(string? attributeArguments = null)
        => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Keyword.@event
            {
                [RpcService]
                public interface IGameWorldAccess;
            }

            namespace Keyword.@event.Ipc
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
                    public static Keyword.@event.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Keyword.@event.Plugin
            {
                using DotBoxD.Abstractions;
                using Keyword.@event;

                [GeneratePluginServer(Context = typeof(RemotePluginContext){{AttributeArgumentPrefix(attributeArguments)}})]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """;

    private static string AttributeArgumentPrefix(string? attributeArguments)
        => string.IsNullOrWhiteSpace(attributeArguments)
            ? string.Empty
            : ", " + attributeArguments;
}
