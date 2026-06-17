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
    {
        var kernelType = GamePluginAssembly().GetType(kernelTypeName, throwOnError: true)!;
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

    public static TheoryData<string, string, string, string?, string?, string[]> GeneratedPackages()
        => new()
        {
            {
                "DotBoxD.Kernels.Game.Plugin.Kernels.GuardianKernel",
                "guardian",
                "IEventKernel<MonsterAggroEvent>",
                "MonsterAggroEvent",
                null,
                ["dotboxd.runtime.async", "host.message.write"]
            },
            {
                "DotBoxD.Kernels.Game.Plugin.Kernels.RetaliationKernel",
                "retaliation",
                "IEventKernel<AttackEvent>",
                "AttackEvent",
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
            }
        };

    private static Assembly GamePluginAssembly()
        => Assembly.LoadFrom(GamePluginAssemblyPath());

    private static string GamePluginAssemblyPath()
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
            "Examples.GameServer.Plugin",
            "bin",
            configuration,
            "net10.0",
            "Examples.GameServer.Plugin.dll"));
    }
}
