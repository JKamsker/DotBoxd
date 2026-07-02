using DotBoxD.Kernels.Game.Plugin.Client.Kernels;
using DotBoxD.Kernels.Game.Shared;

namespace DotBoxD.Kernels.Game.Plugin.Client;

internal static class PackageExport
{
    public static void ExportAll(string pluginsRoot)
    {
        PackageExporter.Export(typeof(BountyClaimKernel), pluginsRoot, "bounty-hunter/client/extensions/bounty-claim.json");
        PackageExporter.Export(typeof(MonsterDeathFxKernel), pluginsRoot, "bounty-hunter/client/hooks/monster-death-fx.json");
        PackageExporter.Export(typeof(GoldHudKernel), pluginsRoot, "bounty-hunter/client/subscriptions/gold-hud.json");
        PackageExporter.Export(typeof(GoldCheatClientKernel), pluginsRoot, "gold-cheat/client/extensions/gold-cheat.json");
        CopyAsset(pluginsRoot, "bounty-hunter/client/assets/skull.anim.txt");
    }

    private static void CopyAsset(string pluginsRoot, string relativePath)
    {
        var destination = Path.GetFullPath(Path.Combine(pluginsRoot, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "assets", "skull.anim.txt"), destination, overwrite: true);
    }
}
