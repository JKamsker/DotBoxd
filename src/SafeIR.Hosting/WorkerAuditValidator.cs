namespace SafeIR.Hosting;

using SafeIR;

internal static class WorkerAuditValidator
{
    private static readonly DateTimeOffset EarliestAcceptedTimestamp = DateTimeOffset.UnixEpoch;
    private static readonly HashSet<string> CommonRunSummaryFields = [
        "mode",
        "executionMode",
        "executionDispatched",
        "cacheStatus",
        "moduleHash",
        "planHash",
        "policyId",
        "policyHash",
        "bindingManifestHash",
        "fuelUsed",
        "maxFuel",
        "loopIterations",
        "maxLoopIterations",
        "allocatedBytes",
        "allocationCharged",
        "maxAllocatedBytes",
        "hostCalls",
        "maxHostCalls",
        "fileBytesRead",
        "maxFileBytesRead",
        "fileBytesWritten",
        "maxFileBytesWritten",
        "networkBytesRead",
        "maxNetworkBytesRead",
        "networkBytesWritten",
        "maxNetworkBytesWritten",
        "logEvents",
        "maxLogEvents",
        "collectionElements",
        "maxCollectionElements",
        "stringBytes",
        "maxStringBytes"
    ];

    public static bool Matches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxAuditEvent auditEvent)
    {
        if (string.IsNullOrWhiteSpace(auditEvent.Kind) ||
            !TextIsSafe(auditEvent.Kind) ||
            !TextIsSafe(auditEvent.ResourceId) ||
            !TextIsSafe(auditEvent.Message) ||
            auditEvent.Bytes is < 0 ||
            (auditEvent.ErrorCode is { } code && !Enum.IsDefined(code)) ||
            (auditEvent.Success && auditEvent.ErrorCode is not null) ||
            !TimestampMatches(plan, auditEvent.Timestamp))
        {
            return false;
        }

        return auditEvent.Kind switch
        {
            "RunSummary" => RunSummarySchemaMatches(plan, auditEvent),
            "WorkerExecution" => ModuleAuditMatches(plan, auditEvent),
            "DebugTrace" => options.EnableDebugTrace && ModuleAuditMatches(plan, auditEvent),
            "CacheInvalidated" => auditEvent.Success && ModuleAuditMatches(plan, auditEvent),
            "PolicyDenied" => PolicyDeniedAuditMatches(auditEvent),
            "BindingCall" or "SandboxLog" or "PluginMessage" => false,
            _ => false
        };
    }

    private static bool TimestampMatches(ExecutionPlan plan, DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero || timestamp < EarliestAcceptedTimestamp)
        {
            return false;
        }

        if (plan.Policy.Deterministic && plan.Policy.LogicalNow is { } logicalNow)
        {
            return timestamp == logicalNow;
        }

        return timestamp <= DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private static bool RunSummarySchemaMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (auditEvent.BindingId is not null ||
            auditEvent.CapabilityId is not null ||
            auditEvent.Effect != SandboxEffect.None ||
            auditEvent.Fields is null ||
            !string.Equals(auditEvent.ResourceId, $"module:{plan.ModuleHash}", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var field in auditEvent.Fields)
        {
            if (!FieldNameAllowed(plan, field.Key) ||
                string.IsNullOrWhiteSpace(field.Key) ||
                !TextIsSafe(field.Key) ||
                !TextIsSafe(field.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FieldNameAllowed(ExecutionPlan plan, string key)
        => CommonRunSummaryFields.Contains(key) ||
           (plan.Policy.Deterministic && key == "logicalNow") ||
           key is "runtimeForm" or "cacheKey" or "artifactHash" or "materializationStatus";

    private static bool ModuleAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => auditEvent.BindingId is null &&
           auditEvent.CapabilityId is null &&
           auditEvent.Effect == SandboxEffect.None &&
           auditEvent.Fields is null &&
           string.Equals(auditEvent.ResourceId, $"module:{plan.ModuleHash}", StringComparison.Ordinal);

    private static bool PolicyDeniedAuditMatches(SandboxAuditEvent auditEvent)
        => !auditEvent.Success &&
           auditEvent.BindingId is null &&
           !string.IsNullOrWhiteSpace(auditEvent.CapabilityId) &&
           auditEvent.Effect == SandboxEffect.None &&
           auditEvent.ErrorCode == SandboxErrorCode.PolicyDenied &&
           string.Equals(auditEvent.ResourceId, $"capability:{auditEvent.CapabilityId}", StringComparison.Ordinal);

    private static bool TextIsSafe(string? value)
        => value is null || value.All(c => !char.IsControl(c));
}
