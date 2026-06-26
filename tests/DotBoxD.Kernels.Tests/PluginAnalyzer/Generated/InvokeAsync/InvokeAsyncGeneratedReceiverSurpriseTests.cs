using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGeneratedReceiverSurpriseTests
{
    [Fact]
    public void Explicit_capture_bag_sync_out_local_avoids_user_local_collision()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Capture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static ValueTask<int> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    bag.LastHealth = world.GetHealth(bag.MonsterId);
                    var __syncOut_LastHealth = 42;
                    return bag.LastHealth + __syncOut_LastHealth;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("__syncOut_LastHealth_0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Implicit_captured_collection_transitive_alias_mutation_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels, System.Collections.Generic.List<int> values)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.List<int> alias = [];
                    alias = values;
                    var transitive = alias;
                    transitive.Add(world.GetHealth("monster-1"));
                    return transitive.Count;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("captured collection 'values'", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_receiver_fallback_honors_explicit_generic_return_type()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<long> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync<long>(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\\\"returnType\\\":\\\"I64\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_receiver_capture_fallback_honors_explicit_generic_return_type()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Capture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static ValueTask<long> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync<Capture, long>(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    bag.LastHealth = world.GetHealth(bag.MonsterId);
                    return bag.LastHealth;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\\\"returnType\\\":{\\\"name\\\":\\\"Record\\\",\\\"arguments\\\":[\\\"I64\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Unqualified_InvokeAsync_inside_generated_facade_is_lowered()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public ValueTask<int> Probe()
                    => InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Actual_unrelated_server_interface_is_not_treated_as_generated_receiver()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public ValueTask<int> Probe(IGameServer server)
                    => server.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    });
            """, """
            public interface IGameServer
            {
                ValueTask<int> InvokeAsync(Func<IGameWorldAccess, ValueTask<int>> lambda);
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    private static string GeneratedFacadeBodySource(string serverMembers, string worldMembers = "")
        => """
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
                [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetHealth(string entityId);
            }
        """ + worldMembers + """
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
            public partial class RemotePluginServer : IGameWorldAccess
            {
        """ + serverMembers + """
            }

            public sealed partial class RemotePluginContext;
        }
        """;
}
