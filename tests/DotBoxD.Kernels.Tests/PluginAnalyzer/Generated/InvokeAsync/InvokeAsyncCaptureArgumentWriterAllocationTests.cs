using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncCaptureArgumentWriterAllocationTests
{
    [Fact]
    public void Captured_collection_arguments_generate_direct_array_writers()
    {
        var result = RunGeneratorAndAssertCompiles(CollectionCaptureSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("global::System.Linq.Enumerable.Select(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("global::System.Linq.Enumerable.SelectMany(", source, StringComparison.Ordinal);
        Assert.Contains("global::System.Array.Empty<global::DotBoxD.Plugins.KernelRpcValue>()", source, StringComparison.Ordinal);
        Assert.Contains("new global::DotBoxD.Plugins.KernelRpcValue[__dotboxd_count0]", source, StringComparison.Ordinal);
        Assert.Contains("new global::DotBoxD.Plugins.KernelRpcValue[__dotboxd_entryCount0]", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var __dotboxd_pair0", source, StringComparison.Ordinal);
    }

    private const string CollectionCaptureSource = """
        using System;
        using System.Collections.Generic;
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
            [RpcService]
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.sumValues", "game.world.values.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int SumValues(List<int> values);

                [HostBinding("host.world.sumScores", "game.world.scores.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int SumScores(Dictionary<string, int> scores);
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
                public static ValueTask<int> Run(
                    RemotePluginServer kernels,
                    List<int> values,
                    Dictionary<string, int> scores)
                    => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return world.SumValues(values) + world.SumScores(scores);
                    });
            }
        }
        """;
}
