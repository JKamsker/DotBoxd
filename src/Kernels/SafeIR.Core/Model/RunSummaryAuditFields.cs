namespace SafeIR;

public static class RunSummaryAuditFields
{
    public static IReadOnlyDictionary<string, string> Create(
        ExecutionPlan plan,
        ResourceMeter budget,
        ExecutionMode mode,
        string cacheStatus,
        string? runtimeForm = null,
        string? cacheKey = null,
        string? artifactHash = null,
        string? materializationStatus = null,
        bool executionDispatched = true)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = mode.ToString(),
            ["executionMode"] = mode.ToString(),
            ["executionDispatched"] = executionDispatched.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cacheStatus"] = cacheStatus,
            ["moduleHash"] = plan.ModuleHash,
            ["planHash"] = plan.PlanHash,
            ["policyId"] = SafePolicyId(plan.Policy.PolicyId),
            ["policyHash"] = plan.PolicyHash,
            ["bindingManifestHash"] = plan.BindingManifestHash,
            ["fuelUsed"] = budget.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxFuel"] = budget.Limits.MaxFuel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["loopIterations"] = budget.LoopIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxLoopIterations"] = budget.Limits.MaxLoopIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allocatedBytes"] = budget.AllocatedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allocationCharged"] = budget.AllocatedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxAllocatedBytes"] = budget.Limits.MaxAllocatedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["hostCalls"] = budget.HostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxHostCalls"] = budget.Limits.MaxHostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["fileBytesRead"] = budget.FileBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxFileBytesRead"] = budget.Limits.MaxFileBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["fileBytesWritten"] = budget.FileBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxFileBytesWritten"] = budget.Limits.MaxFileBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["networkBytesRead"] = budget.NetworkBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxNetworkBytesRead"] = budget.Limits.MaxNetworkBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["networkBytesWritten"] = budget.NetworkBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxNetworkBytesWritten"] = budget.Limits.MaxNetworkBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["logEvents"] = budget.LogEvents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxLogEvents"] = budget.Limits.MaxLogEvents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["collectionElements"] = budget.CollectionElements.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxCollectionElements"] = budget.Limits.MaxTotalCollectionElements.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["stringBytes"] = budget.StringBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxStringBytes"] = budget.Limits.MaxTotalStringBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        AddIfPresent(fields, "runtimeForm", runtimeForm);
        AddIfPresent(fields, "cacheKey", cacheKey);
        AddIfPresent(fields, "artifactHash", artifactHash);
        AddIfPresent(fields, "materializationStatus", materializationStatus);
        return fields;
    }

    private static void AddIfPresent(IDictionary<string, string> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[key] = value;
        }
    }

    private static string SafePolicyId(string? policyId)
    {
        if (string.IsNullOrWhiteSpace(policyId))
        {
            return "[redacted]";
        }

        var sanitized = new string(policyId.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (sanitized.Length is 0 or > 128 ||
            ContainsSecretMarker(sanitized) ||
            sanitized.Contains("://", StringComparison.Ordinal) ||
            sanitized.Contains('/') ||
            sanitized.Contains('\\') ||
            sanitized.Any(c => !IsPolicyIdChar(c)))
        {
            return "[redacted]";
        }

        return sanitized;
    }

    private static bool IsPolicyIdChar(char c)
        => char.IsAsciiLetterOrDigit(c) ||
           c is '-' or '_' or '.' or ':';

    private static bool ContainsSecretMarker(string value)
    {
        var normalized = value.ToLowerInvariant();
        return normalized.Contains("authorization", StringComparison.Ordinal) ||
               normalized.Contains("bearer", StringComparison.Ordinal) ||
               normalized.Contains("password", StringComparison.Ordinal) ||
               normalized.Contains("passwd", StringComparison.Ordinal) ||
               normalized.Contains("secret", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("api-key", StringComparison.Ordinal) ||
               normalized.Contains("apikey", StringComparison.Ordinal) ||
               normalized.Contains("client_key", StringComparison.Ordinal) ||
               normalized.Contains("client-secret", StringComparison.Ordinal) ||
               normalized.Contains("client_secret", StringComparison.Ordinal) ||
               normalized.Contains("private-key", StringComparison.Ordinal) ||
               normalized.Contains("private_key", StringComparison.Ordinal);
    }
}
