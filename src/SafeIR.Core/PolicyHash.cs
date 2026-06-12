namespace SafeIR;

internal static class PolicyHash
{
    public static string Compute(SandboxPolicy policy)
    {
        var records = new List<string> {
            CanonicalEncoding.Record(
                "policy-v2",
                policy.PolicyId,
                Format((long)policy.AllowedEffects),
                policy.Deterministic.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Format(policy.LogicalNow?.ToUnixTimeMilliseconds()),
                Format(policy.RandomSeed)),
            ResourceLimitRecord(policy.ResourceLimits)
        };

        records.AddRange(policy.Grants.Select(GrantRecord).Order(StringComparer.Ordinal));
        return CanonicalEncoding.HashRecords(records);
    }

    private static string ResourceLimitRecord(ResourceLimits limits)
        => CanonicalEncoding.Record(
            "limits",
            Format(limits.MaxFuel),
            Format(limits.MaxLoopIterations),
            Format(limits.EffectiveWallTime.Ticks),
            Format(limits.MaxAllocatedBytes),
            Format(limits.MaxCallDepth),
            Format(limits.MaxHostCalls),
            Format(limits.MaxListLength),
            Format(limits.MaxMapEntries),
            Format(limits.MaxCollectionDepth),
            Format(limits.MaxTotalCollectionElements),
            Format(limits.MaxFileBytesRead),
            Format(limits.MaxFileBytesWritten),
            Format(limits.MaxNetworkBytesRead),
            Format(limits.MaxNetworkBytesWritten),
            Format(limits.MaxLogEvents),
            Format(limits.MaxLogMessageLength),
            Format(limits.MaxStringLength),
            Format(limits.MaxTotalStringBytes));

    private static string GrantRecord(CapabilityGrant grant)
    {
        var fields = new List<string?> {
            "grant",
            grant.Id,
            Format(grant.ExpiresAt?.ToUnixTimeMilliseconds()),
            grant.GrantedBy,
            grant.Reason
        };
        fields.AddRange(grant.Parameters
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => CanonicalEncoding.Record("param", p.Key, p.Value)));
        return CanonicalEncoding.Record(fields);
    }

    private static string? Format(long? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string? Format(ulong? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
