using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncSurpriseGenerationTests
{
    [Fact]
    public void Explicit_capture_bag_accepts_reordered_named_arguments()
    {
        var result = RunGenerator(CaptureBagSource("""
            public sealed class MonsterCapture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static class Usage
            {
                public static ValueTask<string> Run(RemotePluginServer kernels, MonsterCapture captures)
                    => kernels.InvokeAsync(
                        lambda: async (IGameWorldAccess world, MonsterCapture bag) =>
                        {
                            var monster = world.GetMonster(bag.MonsterId);
                            bag.LastHealth = monster.Health;
                            return monster.Name;
                        },
                        captures: captures);
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("\"parameters\\\":[{\\\"name\\\":\\\"bag\\\"", source, StringComparison.Ordinal);
        Assert.Contains("captures.LastHealth =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_bag_property_with_private_setter_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(CaptureBagSource("""
            public sealed class MonsterCapture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; private set; }

                public ValueTask<int> Run(RemotePluginServer kernels)
                    => kernels.InvokeAsync(this, async (IGameWorldAccess world, MonsterCapture bag) =>
                    {
                        bag.LastHealth = world.GetHealth(bag.MonsterId);
                        return bag.LastHealth;
                    });
            }
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("accessible set accessor", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_capture_bag_reads_sync_out_local_after_assignment()
    {
        var result = RunGenerator(CaptureBagSource("""
            public sealed class MonsterCapture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static class Usage
            {
                public static ValueTask<int> Run(RemotePluginServer kernels, MonsterCapture captures)
                    => kernels.InvokeAsync(captures, async (IGameWorldAccess world, MonsterCapture bag) =>
                    {
                        bag.LastHealth = world.GetHealth(bag.MonsterId);
                        return bag.LastHealth;
                    });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));
        var syncOutReads = source.Split("\\\"var\\\":\\\"__syncOut_LastHealth\\\"").Length - 1;

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.True(syncOutReads >= 2, source);
    }

    [Fact]
    public void Explicit_capture_bag_applies_numeric_conversion_to_sync_out_reads()
    {
        var result = RunGenerator(CaptureBagSource("""
            public sealed class MonsterCapture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static class Usage
            {
                public static ValueTask<long> Run(RemotePluginServer kernels, MonsterCapture captures)
                    => kernels.InvokeAsync(captures, async (IGameWorldAccess world, MonsterCapture bag) =>
                    {
                        bag.LastHealth = world.GetHealth(bag.MonsterId);
                        long health = bag.LastHealth;
                        return health;
                    });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_capture_bag_supports_public_field_sync_out()
    {
        var result = RunGenerator(CaptureBagSource("""
            public sealed class MonsterCapture
            {
                public string MonsterId = "";
                public int LastHealth;
            }

            public static class Usage
            {
                public static ValueTask<int> Run(RemotePluginServer kernels, MonsterCapture captures)
                    => kernels.InvokeAsync(captures, async (IGameWorldAccess world, MonsterCapture bag) =>
                    {
                        bag.LastHealth = world.GetHealth(bag.MonsterId);
                        return bag.LastHealth;
                    });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));
        var syncOutReads = source.Split("\\\"var\\\":\\\"__syncOut_LastHealth\\\"").Length - 1;

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("captures.LastHealth =", source, StringComparison.Ordinal);
        Assert.True(syncOutReads >= 2, source);
    }

    [Fact]
    public void Implicit_captured_list_mutation_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels, System.Collections.Generic.List<int> values)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    values.Add(world.GetHealth("monster-1"));
                    return values.Count;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("captured collection 'values'", StringComparison.Ordinal));
    }

    [Fact]
    public void Implicit_captured_map_index_mutation_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels, System.Collections.Generic.Dictionary<string, int> scores)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    scores["monster-1"] = world.GetHealth("monster-1");
                    return scores.Count;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("captured map 'scores'", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_capture_bag_collection_alias_mutation_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(CaptureBagSource("""
            public sealed class MonsterCapture
            {
                public System.Collections.Generic.List<int> Values { get; set; } = [];
            }

            public static class Usage
            {
                public static ValueTask<int> Run(RemotePluginServer kernels, MonsterCapture captures)
                    => kernels.InvokeAsync(captures, async (IGameWorldAccess world, MonsterCapture bag) =>
                    {
                        var values = bag.Values;
                        values.Add(world.GetHealth("monster-1"));
                        return values.Count;
                    });
            }
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("captured collection 'Values'", StringComparison.Ordinal));
    }

    [Fact]
    public void Erased_IPluginServer_receiver_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(IPluginServer<IGameWorldAccess> kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("IPluginServer", StringComparison.Ordinal));
    }

    [Fact]
    public void Conditional_access_InvokeAsync_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int>? Run(RemotePluginServer? kernels)
                => kernels?.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("conditional access", StringComparison.Ordinal));
    }

    private static string CaptureBagSource(string sampleDeclarations)
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
            public sealed record MonsterSnapshot(string Id, string Name, int Health);

            [DotBoxDService]
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getMonster", "game.world.monster.read.snapshot", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
                MonsterSnapshot GetMonster(string entityId);

                [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetHealth(string entityId);
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
        """ + sampleDeclarations + """
        }
        """;
}
