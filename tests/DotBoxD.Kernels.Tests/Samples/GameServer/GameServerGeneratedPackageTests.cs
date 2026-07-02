using System.Reflection;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GameServerGeneratedPackageTests
{
    [Theory]
    [MemberData(nameof(GeneratedPackages))]
    public void GameServer_plugin_generated_packages_resolve_and_round_trip(
        string kernelTypeName,
        string pluginId,
        string contract,
        string? subscription,
        string? rpcEntrypoint,
        string[] requiredCapabilities)
        => AssertGeneratedPackage(
            GamePluginAssembly(),
            kernelTypeName,
            pluginId,
            contract,
            subscription,
            rpcEntrypoint,
            requiredCapabilities);

    public static TheoryData<string, string, string, string?, string?, string[]> GeneratedPackages()
        => new()
        {
            {
                "DotBoxD.Kernels.Game.Plugin.Kernels.GuardianKernel",
                "guardian",
                "IEventKernel<DotBoxD.Kernels.Game.Server.Abstractions.Events.MonsterAggroEvent>",
                "DotBoxD.Kernels.Game.Server.Abstractions.Events.MonsterAggroEvent",
                null,
                ["dotboxd.runtime.async", "host.message.write"]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Kernels.RetaliationKernel",
                "retaliation",
                "IEventKernel<DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent>",
                "DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent",
                null,
                ["dotboxd.runtime.async", "host.message.write"]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Kernels.MonsterKillerKernel",
                "monster-killer",
                "MonsterKillerKernel",
                null,
                "KillMonstersAsync",
                [
                    "game.world.entity.read.health",
                    "game.world.entity.read.level",
                    "game.world.entity.read.position",
                    "game.world.monster.read.kind",
                    "game.world.monster.write.kill"
                ]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Kernels.RangeMonsterKillerKernel",
                "range-monster-killer",
                "RangeMonsterKillerKernel",
                null,
                "KillMonstersInRangeAsync",
                [
                    "game.world.entity.read.health",
                    "game.world.entity.read.level",
                    "game.world.entity.read.position",
                    "game.world.monster.read.kind",
                    "game.world.monster.write.kill"
                ]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Kernels.BlinkKernel",
                "blink",
                "BlinkKernel",
                null,
                "BlinkBehindAsync",
                [
                    "game.world.combat.threat",
                    "game.world.entity.read.position",
                    "game.world.monster.write.position"
                ]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Kernels.BountyPayoutKernel",
                "bounty.claim",
                "BountyPayoutKernel",
                null,
                "ClaimAsync",
                [
                    "dotboxd.runtime.async",
                    "game.world.entity.read.health",
                    "game.world.entity.read.level",
                    "game.world.gold.read.claimable",
                    "game.world.gold.write.grant",
                    "game.world.monster.read.kind"
                ]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Kernels.GoldCheatKernel",
                "gold.cheat",
                "GoldCheatKernel",
                null,
                "CheatAsync",
                ["dotboxd.runtime.async", "game.world.gold.write.grant"]
            }
        };

    [Theory]
    [MemberData(nameof(ClientGeneratedPackages))]
    public void GameServer_client_plugin_generated_packages_resolve_and_round_trip(
        string kernelTypeName,
        string pluginId,
        string contract,
        string? subscription,
        string? rpcEntrypoint,
        string[] requiredCapabilities)
        => AssertGeneratedPackage(
            GamePluginClientAssembly(),
            kernelTypeName,
            pluginId,
            contract,
            subscription,
            rpcEntrypoint,
            requiredCapabilities);

    public static TheoryData<string, string, string, string?, string?, string[]> ClientGeneratedPackages()
        => new()
        {
            {
                "DotBoxD.Kernels.Game.Plugin.Client.Kernels.BountyClaimKernel",
                "bounty.claim.client",
                "BountyClaimKernel",
                null,
                "ClaimAsync",
                ["dotboxd.runtime.async", "game.client.server.call", "game.client.ui.write"]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Client.Kernels.MonsterDeathFxKernel",
                "monster-death-fx",
                "IEventKernel<DotBoxD.Kernels.Game.Client.Abstractions.Events.ClientMonsterKilledEvent>",
                "DotBoxD.Kernels.Game.Client.Abstractions.Events.ClientMonsterKilledEvent",
                null,
                ["dotboxd.runtime.async", "host.message.write"]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Client.Kernels.GoldHudKernel",
                "gold-hud",
                "IEventKernel<DotBoxD.Kernels.Game.Client.Abstractions.Events.ClientGoldChangedEvent>",
                "DotBoxD.Kernels.Game.Client.Abstractions.Events.ClientGoldChangedEvent",
                null,
                ["dotboxd.runtime.async", "host.message.write"]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Client.Kernels.GoldCheatClientKernel",
                "gold.cheat.client",
                "GoldCheatClientKernel",
                null,
                "CheatAsync",
                ["dotboxd.runtime.async", "game.client.server.call"]
            }
        };

    [Fact]
    public void GameServer_plugin_export_materializes_two_half_bundle_layout()
    {
        var pluginsRoot = Path.Combine(Path.GetTempPath(), "dotboxd-game-bundles-" + Guid.NewGuid().ToString("N"));
        try
        {
            InvokeExport(GamePluginAssembly(), "DotBoxD.Kernels.Game.Plugin.PackageExport", pluginsRoot);
            InvokeExport(GamePluginClientAssembly(), "DotBoxD.Kernels.Game.Plugin.Client.PackageExport", pluginsRoot);

            var expectedJson = new[]
            {
                "bounty-hunter/client/extensions/bounty-claim.json",
                "bounty-hunter/client/hooks/monster-death-fx.json",
                "bounty-hunter/client/subscriptions/gold-hud.json",
                "bounty-hunter/server/extensions/bounty-payout.json",
                "gold-cheat/client/extensions/gold-cheat.json",
                "gold-cheat/server/extensions/gold-cheat.json",
                "guardian/server/hooks/guardian.json",
                "guardian/server/subscriptions/retaliation.json"
            };
            var actualJson = Directory.EnumerateFiles(pluginsRoot, "*.json", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(pluginsRoot, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedJson, actualJson);
            foreach (var relativePath in actualJson)
            {
                var package = PluginPackageJsonSerializer.Import(
                    File.ReadAllText(Path.Combine(pluginsRoot, relativePath)));
                Assert.False(string.IsNullOrWhiteSpace(package.Manifest.PluginId));
            }

            Assert.True(File.Exists(Path.Combine(
                pluginsRoot,
                "bounty-hunter",
                "client",
                "assets",
                "skull.anim.txt")));
        }
        finally
        {
            if (Directory.Exists(pluginsRoot))
            {
                Directory.Delete(pluginsRoot, recursive: true);
            }
        }
    }

    private static Assembly GamePluginAssembly()
        => Assembly.LoadFrom(GamePluginAssemblyPath());

    private static Assembly GamePluginClientAssembly()
        => Assembly.LoadFrom(GamePluginClientAssemblyPath());

    private static void InvokeExport(Assembly assembly, string typeName, string pluginsRoot)
        => assembly
            .GetType(typeName, throwOnError: true)!
            .GetMethod("ExportAll", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [pluginsRoot]);

    private static void AssertGeneratedPackage(
        Assembly assembly,
        string kernelTypeName,
        string pluginId,
        string contract,
        string? subscription,
        string? rpcEntrypoint,
        string[] requiredCapabilities)
    {
        var kernelType = assembly.GetType(kernelTypeName, throwOnError: true)!;
        var package = KernelPackageRegistry.Resolve(kernelType);

        Assert.Equal(pluginId, package.Manifest.PluginId);
        Assert.Equal(contract, package.Manifest.Contract);
        Assert.Equal(rpcEntrypoint, package.Manifest.RpcEntrypoint);
        Assert.Equal(
            subscription is null ? [] : [subscription],
            package.Manifest.Subscriptions.Select(item => item.Event).ToArray());
        foreach (var capability in requiredCapabilities)
        {
            Assert.Contains(capability, package.Manifest.RequiredCapabilities, StringComparer.Ordinal);
        }

        var imported = PluginPackageJsonSerializer.Import(PluginPackageJsonSerializer.Export(package));
        Assert.Equal(package.Manifest.PluginId, imported.Manifest.PluginId);
        Assert.Equal(package.Manifest.Contract, imported.Manifest.Contract);
        Assert.Equal(package.Manifest.RpcEntrypoint, imported.Manifest.RpcEntrypoint);
        Assert.Equal(package.Manifest.RequiredCapabilities, imported.Manifest.RequiredCapabilities);
    }

    private static string GamePluginAssemblyPath()
        => SampleAssemblyPath("Examples.GameServer.Plugin", "Examples.GameServer.Plugin.dll");

    private static string GamePluginClientAssemblyPath()
        => SampleAssemblyPath("Examples.GameServer.Plugin.Client", "Examples.GameServer.Plugin.Client.dll");

    private static string SampleAssemblyPath(string projectFolder, string dllName)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configuration = output.Parent!.Name;
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "GameServer",
            projectFolder,
            "bin",
            configuration,
            "net10.0",
            dllName));
    }
}
