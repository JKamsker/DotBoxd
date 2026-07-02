using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Shared;

namespace DotBoxD.Kernels.Game.Plugin;

internal static class PackageExport
{
    public static void ExportAll(string pluginsRoot)
    {
        PackageExporter.Export(typeof(GuardianKernel), pluginsRoot, "guardian/server/hooks/guardian.json");
        PackageExporter.Export(typeof(RetaliationKernel), pluginsRoot, "guardian/server/subscriptions/retaliation.json");
        PackageExporter.Export(typeof(BountyPayoutKernel), pluginsRoot, "bounty-hunter/server/extensions/bounty-payout.json");
        PackageExporter.Export(typeof(GoldCheatKernel), pluginsRoot, "gold-cheat/server/extensions/gold-cheat.json");
    }
}
