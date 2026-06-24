using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGenerationTests
{
    [Fact]
    public void Block_body_no_capture_lambda_generates_anonymous_package()
    {
        var result = RunGenerator(NoCaptureSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("$anon:", source, StringComparison.Ordinal);
        Assert.Contains("host.world.getHealth", source, StringComparison.Ordinal);
        Assert.Contains("game.world.monster.read.health", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Object_snapshot_member_access_generates_record_get_package()
    {
        var result = RunGenerator(ObjectSurfaceSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("host.world.getMonster", source, StringComparison.Ordinal);
        Assert.Contains("game.world.monster.read.snapshot", source, StringComparison.Ordinal);
        Assert.Contains("record.get", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Expression_body_lambda_is_ignored()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync((IGameWorldAccess world) => new ValueTask<int>(world.GetHealth("monster-1")));
            """));

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("InvokeAsync_", StringComparison.Ordinal));
    }

    [Fact]
    public void Implicit_capture_generates_reflection_arguments_and_sync_out()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
            {
                var monsterId = "monster-1";
                var lastHealth = 0;
                return kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    lastHealth = world.GetHealth(monsterId);
                    return lastHealth;
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("__ReadCapture<", source, StringComparison.Ordinal);
        Assert.Contains("__WriteCapture(lambda, \"lastHealth\"", source, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\":\\\"monsterId\\\"", source, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\":\\\"lastHealth\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_capture_bag_generates_sync_in_and_sync_out_package()
    {
        var result = RunGenerator(CaptureBagSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("\"parameters\\\":[{\\\"name\\\":\\\"bag\\\"", source, StringComparison.Ordinal);
        Assert.Contains("__syncOut_LastHealth", source, StringComparison.Ordinal);
        Assert.Contains("captures.LastHealth =", source, StringComparison.Ordinal);
        Assert.Contains("__result.ItemCount", source, StringComparison.Ordinal);
        Assert.Contains("__result.GetItem(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__result.Items", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Multiple_complex_InvokeAsync_results_generate_unique_reader_helpers()
    {
        var result = RunGenerator(
            """
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
                    ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default);
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
                    public static async ValueTask<int> Run(RemotePluginServer kernels)
                    {
                        var first = await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                        {
                            return world.GetMonster("monster-1");
                        });
                        var second = await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                        {
                            return world.GetMonster("monster-2");
                        });
                        return first.Health + second.Health;
                    }
                }
            }
            """);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));
        var helpers = Regex.Matches(
                source,
                @"private static .* (ReadInvokeAsyncResult_InvokeAsync_[A-Za-z0-9_]+_0)\(")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(2, helpers.Length);
        Assert.Equal(2, helpers.Distinct(StringComparer.Ordinal).Count());
    }
}
