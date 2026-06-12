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
        string? artifactHash = null)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = mode.ToString(),
            ["cacheStatus"] = cacheStatus,
            ["moduleHash"] = plan.ModuleHash,
            ["planHash"] = plan.PlanHash,
            ["policyHash"] = plan.PolicyHash,
            ["bindingManifestHash"] = plan.BindingManifestHash,
            ["fuelUsed"] = budget.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxFuel"] = budget.Limits.MaxFuel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["loopIterations"] = budget.LoopIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxLoopIterations"] = budget.Limits.MaxLoopIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allocatedBytes"] = budget.AllocatedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
        return fields;
    }

    private static void AddIfPresent(IDictionary<string, string> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[key] = value;
        }
    }
}
