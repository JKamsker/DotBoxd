namespace SafeIR.Compiler.Internal;

/// <summary>
/// Bounded retention policy for the compiled cache <c>quarantine</c> tree.
///
/// Invalid cache entries are moved aside into a uniquely named quarantine directory so they can be
/// inspected without blocking recompilation. Each corruption produces one retained payload, so with
/// no retention policy a long-lived host or CI cache that repeatedly sees corrupted/stale artifacts
/// grows the quarantine tree without bound, inflating disk usage and directory-scan cost.
///
/// This helper keeps the most recent <see cref="MaxRetainedEntries"/> quarantined payloads (the ones
/// most useful for troubleshooting) and deletes the older ones. It runs after each quarantine move,
/// so cleanup cost stays proportional to the current quarantine size rather than the total number of
/// entries ever quarantined over the cache lifetime.
/// </summary>
internal static class PersistentCompiledArtifactCacheQuarantine
{
    /// <summary>
    /// Maximum number of quarantined payloads retained for diagnostics. Generous enough to keep a
    /// useful troubleshooting window while bounding growth regardless of corruption volume.
    /// </summary>
    public const int MaxRetainedEntries = 8;

    public static void Prune(string quarantineRoot)
        => Prune(quarantineRoot, MaxRetainedEntries);

    public static void Prune(string quarantineRoot, int maxRetainedEntries)
    {
        if (maxRetainedEntries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetainedEntries));
        }

        if (!Directory.Exists(quarantineRoot))
        {
            return;
        }

        var entries = new List<DirectoryInfo>();
        foreach (var directory in Directory.EnumerateDirectories(quarantineRoot))
        {
            entries.Add(new DirectoryInfo(directory));
        }

        if (entries.Count <= maxRetainedEntries)
        {
            return;
        }

        // Newest first, so the retained prefix is the most recent payloads. CreationTimeUtc reflects
        // when the entry was quarantined; fall back to the name (which embeds a unix-millis stamp)
        // for a stable, deterministic order when timestamps collide.
        entries.Sort(static (left, right) =>
        {
            var byTime = right.CreationTimeUtc.CompareTo(left.CreationTimeUtc);
            return byTime != 0 ? byTime : StringComparer.Ordinal.Compare(right.Name, left.Name);
        });

        for (var i = maxRetainedEntries; i < entries.Count; i++)
        {
            TryDelete(entries[i]);
        }
    }

    private static void TryDelete(DirectoryInfo entry)
    {
        try
        {
            entry.Refresh();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                // Never traverse into a reparse point during cleanup; just unlink it.
                entry.Delete(recursive: false);
                return;
            }

            entry.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Pruning is best-effort housekeeping: a concurrently removed or locked payload must not
            // fail the cache read that triggered quarantine. The next prune retries.
        }
    }
}
