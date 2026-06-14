namespace DotBoxd.Hosting;

using DotBoxd.Kernels;

internal static class WorkerEnvelopeValidators
{
    private const int MaxSafeMessageLength = 1_024;
    private const int MaxDiagnosticIdLength = 128;

    internal static bool ErrorMatches(SandboxError? error)
        => error is not null &&
           Enum.IsDefined(error.Code) &&
           IsSafeText(error.SafeMessage, MaxSafeMessageLength, allowColon: true) &&
           IsSafeOptionalDiagnosticId(error.DiagnosticId);

    internal static bool BudgetFieldsMatch(ExecutionPlan plan, SandboxAuditEvent summary)
        => FieldEquals(summary, "maxFuel", plan.Budget.MaxFuel) &&
           FieldEquals(summary, "maxLoopIterations", plan.Budget.MaxLoopIterations) &&
           FieldEquals(summary, "maxAllocatedBytes", plan.Budget.MaxAllocatedBytes) &&
           FieldEquals(summary, "maxHostCalls", plan.Budget.MaxHostCalls) &&
           FieldEquals(summary, "maxFileBytesRead", plan.Budget.MaxFileBytesRead) &&
           FieldEquals(summary, "maxFileBytesWritten", plan.Budget.MaxFileBytesWritten) &&
           FieldEquals(summary, "maxNetworkBytesRead", plan.Budget.MaxNetworkBytesRead) &&
           FieldEquals(summary, "maxNetworkBytesWritten", plan.Budget.MaxNetworkBytesWritten) &&
           FieldEquals(summary, "maxLogEvents", plan.Budget.MaxLogEvents) &&
           FieldEquals(summary, "maxCollectionElements", plan.Budget.MaxTotalCollectionElements) &&
           FieldEquals(summary, "maxStringBytes", plan.Budget.MaxTotalStringBytes);

    private static bool IsSafeOptionalDiagnosticId(string? value)
        => value is null || IsSafeText(value, MaxDiagnosticIdLength, allowColon: false);

    private static bool IsSafeText(string? value, int maxLength, bool allowColon)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || ContainsSecretMarker(value))
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsControl(c) || (!allowColon && c == ':'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FieldEquals(SandboxAuditEvent summary, string key, long value)
        => summary.Fields is not null &&
           summary.Fields.TryGetValue(key, out var actual) &&
           string.Equals(
               actual,
               value.ToString(System.Globalization.CultureInfo.InvariantCulture),
               StringComparison.Ordinal);

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
               normalized.Contains("client-key", StringComparison.Ordinal) ||
               normalized.Contains("client_key", StringComparison.Ordinal) ||
               normalized.Contains("client-secret", StringComparison.Ordinal) ||
               normalized.Contains("client_secret", StringComparison.Ordinal) ||
               normalized.Contains("private-key", StringComparison.Ordinal) ||
               normalized.Contains("private_key", StringComparison.Ordinal);
    }
}
