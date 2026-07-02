namespace DotBoxD.Kernels.Model;

public static class RunSummaryAuditFields
{
    private const string Redacted = "[redacted]";

    private static readonly string[] SecretMarkers =
    [
        "authorization",
        "bearer",
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "access-token",
        "access_token",
        "refresh-token",
        "refresh_token",
        "session-token",
        "session_token",
        "api-key",
        "api_key",
        "apikey",
        "account-key",
        "account_key",
        "client-key",
        "client_key",
        "client-secret",
        "client_secret",
        "private-key",
        "private_key"
    ];

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

    internal static string SafePolicyId(string? policyId)
    {
        if (string.IsNullOrEmpty(policyId))
        {
            return Redacted;
        }

        var start = 0;
        var end = policyId.Length - 1;
        while (start <= end && IsPolicyIdTrimChar(policyId[start]))
        {
            start++;
        }

        while (end >= start && IsPolicyIdTrimChar(policyId[end]))
        {
            end--;
        }

        var length = end - start + 1;
        if (length is <= 0 or > 128)
        {
            return Redacted;
        }

        for (var i = start; i <= end; i++)
        {
            var c = policyId[i];
            if (char.IsControl(c) || !IsPolicyIdChar(c))
            {
                return Redacted;
            }
        }

        if (ContainsSecretMarker(policyId, start, length))
        {
            return Redacted;
        }

        return start == 0 && length == policyId.Length
            ? policyId
            : policyId.Substring(start, length);
    }

    private static bool IsPolicyIdTrimChar(char c)
        => char.IsWhiteSpace(c) || char.IsControl(c);

    private static bool IsPolicyIdChar(char c)
        => char.IsAsciiLetterOrDigit(c) ||
           c is '-' or '_' or '.' or ':';

    private static bool ContainsSecretMarker(string value, int startIndex, int count)
    {
        foreach (var marker in SecretMarkers)
        {
            if (value.IndexOf(marker, startIndex, count, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
