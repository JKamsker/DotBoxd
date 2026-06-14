namespace DotBoxd.Kernels;

/// <summary>
/// Hierarchical capability matching for dotted capability ids (e.g. <c>game.world.monster.health.get</c>).
/// A <em>grant</em> may be a wildcard; a <em>requirement</em> is always a concrete id. A grant of
/// <c>game.world.monster.*</c> matches any required id under <c>game.world.monster.</c> (at least one
/// further segment); a bare <c>*</c> matches everything; otherwise matching is exact.
/// </summary>
public static class CapabilityPattern
{
    private const string MatchAll = "*";
    private const string WildcardSuffix = ".*";

    /// <summary>Whether a granted pattern authorizes a concrete required capability id.</summary>
    public static bool Matches(string grantPattern, string requiredId)
    {
        ArgumentNullException.ThrowIfNull(grantPattern);
        ArgumentNullException.ThrowIfNull(requiredId);

        if (string.Equals(grantPattern, MatchAll, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(grantPattern, requiredId, StringComparison.Ordinal))
        {
            return true;
        }

        if (grantPattern.EndsWith(WildcardSuffix, StringComparison.Ordinal))
        {
            // Keep the trailing dot so "game.world.monster.*" requires a segment after
            // "game.world.monster." and does not match the bare prefix "game.world.monster".
            var prefix = grantPattern[..^1];
            return requiredId.Length > prefix.Length
                && requiredId.StartsWith(prefix, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Whether a capability id is a wildcard pattern (<c>*</c> or ends with <c>.*</c>).</summary>
    public static bool IsWildcard(string capabilityId)
    {
        ArgumentNullException.ThrowIfNull(capabilityId);
        return string.Equals(capabilityId, MatchAll, StringComparison.Ordinal)
            || capabilityId.EndsWith(WildcardSuffix, StringComparison.Ordinal);
    }
}
