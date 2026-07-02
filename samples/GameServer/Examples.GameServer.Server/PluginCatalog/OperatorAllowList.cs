namespace DotBoxD.Kernels.Game.Server.PluginCatalog;

internal sealed class OperatorAllowList
{
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
    {
        ["guardian"] = new(
            ["host.message.write", "dotboxd.runtime.async"],
            []),
        ["bounty-hunter"] = new(
            [
                "dotboxd.runtime.async",
                "game.world.entity.read.",
                "game.world.monster.read.",
                "game.world.gold.read.claimable",
                "game.world.gold.write.grant"
            ],
            ["bounty.claim"])
    };

    public bool AllowsCapabilities(string bundleId, IReadOnlyList<string> capabilities)
        => Entries.TryGetValue(bundleId, out var entry) &&
           capabilities.All(entry.AllowsCapability);

    public bool AllowsOperation(string bundleId, string operation)
        => Entries.TryGetValue(bundleId, out var entry) &&
           entry.ClientOperations.Contains(operation);

    public IReadOnlyList<string> Operations(string bundleId)
        => Entries.TryGetValue(bundleId, out var entry) ? entry.ClientOperations : [];

    private sealed record Entry(string[] CapabilityAllow, string[] ClientOperations)
    {
        public bool AllowsCapability(string capability)
        {
            foreach (var allowed in CapabilityAllow)
            {
                if (allowed.EndsWith(".", StringComparison.Ordinal))
                {
                    if (capability.StartsWith(allowed, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                else if (string.Equals(capability, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
