namespace SafeIR;

public static class BindingAuditFields
{
    public static IReadOnlyDictionary<string, string> Create(
        string resourceKind,
        DateTimeOffset startedAt,
        string moduleHash,
        string policyHash,
        bool deterministic,
        long? bytesRead = null,
        long? bytesWritten = null)
    {
        var fields = Create(resourceKind, startedAt, deterministic, bytesRead, bytesWritten)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        fields["moduleHash"] = moduleHash;
        fields["policyHash"] = policyHash;
        return fields;
    }

    public static IReadOnlyDictionary<string, string> Create(
        string resourceKind,
        DateTimeOffset startedAt,
        bool deterministic = false,
        long? bytesRead = null,
        long? bytesWritten = null)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["resourceKind"] = resourceKind,
            ["durationMs"] = deterministic ? "0.000" : DurationMs(startedAt)
        };

        AddBytes(fields, "bytesRead", bytesRead);
        AddBytes(fields, "bytesWritten", bytesWritten);
        return fields;
    }

    private static void AddBytes(IDictionary<string, string> fields, string key, long? bytes)
    {
        if (bytes is not null)
        {
            fields[key] = bytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static string DurationMs(DateTimeOffset startedAt)
    {
        var elapsed = DateTimeOffset.UtcNow - startedAt;
        var milliseconds = Math.Max(0, elapsed.TotalMilliseconds);
        return milliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
    }
}
