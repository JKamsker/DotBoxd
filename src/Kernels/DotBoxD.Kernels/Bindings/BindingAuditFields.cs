namespace DotBoxD.Kernels;

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
        // Reserve capacity for the two base fields, the optional byte fields, and the
        // module/policy hashes so the final dictionary is built once with no intermediate copy.
        var fields = BuildBaseFields(resourceKind, startedAt, deterministic, bytesRead, bytesWritten, extraCapacity: 2);
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
        => BuildBaseFields(resourceKind, startedAt, deterministic, bytesRead, bytesWritten, extraCapacity: 0);

    internal static Dictionary<string, string> CreateMutable(
        string resourceKind,
        DateTimeOffset startedAt,
        string moduleHash,
        string policyHash,
        bool deterministic,
        int extraCapacity,
        long? bytesRead = null,
        long? bytesWritten = null)
    {
        var fields = BuildBaseFields(resourceKind, startedAt, deterministic, bytesRead, bytesWritten, extraCapacity + 2);
        fields["moduleHash"] = moduleHash;
        fields["policyHash"] = policyHash;
        return fields;
    }

    private static Dictionary<string, string> BuildBaseFields(
        string resourceKind,
        DateTimeOffset startedAt,
        bool deterministic,
        long? bytesRead,
        long? bytesWritten,
        int extraCapacity)
    {
        const int baseFieldCount = 2;
        var optionalByteFields = (bytesRead is null ? 0 : 1) + (bytesWritten is null ? 0 : 1);
        var capacity = baseFieldCount + optionalByteFields + extraCapacity;

        var fields = new Dictionary<string, string>(capacity, StringComparer.Ordinal)
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
