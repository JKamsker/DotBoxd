using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using System.Globalization;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;

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
            !ResultShapeMatches(auditEvent) ||
            !TimestampMatches(plan, auditEvent.Timestamp))
        {
            return false;
        }

        return auditEvent.Kind switch
        {
            "RunSummary" => RunSummarySchemaMatches(plan, auditEvent),
            "WorkerExecution" => ModuleAuditMatches(plan, auditEvent),
            "ExecutionFallback" => ExecutionFallbackAuditMatches(plan, auditEvent),
            "VerifierFailure" => VerifierFailureAuditMatches(plan, auditEvent),
            "DebugTrace" => options.EnableDebugTrace && ModuleAuditMatches(plan, auditEvent),
            "CacheInvalidated" => false,
            "PolicyDenied" => false,
            "BindingCall" or "SandboxLog" or "PluginMessage" => BindingAuditMatches(plan, entrypoint, auditEvent),
            _ => false
        };
    }

    private static bool ResultShapeMatches(SandboxAuditEvent auditEvent)
        => !auditEvent.Success ||
           auditEvent.ErrorCode is null ||
           auditEvent.Kind == "ExecutionFallback";

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

    private static bool ExecutionFallbackAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => auditEvent.Success &&
           auditEvent.ErrorCode is SandboxErrorCode.ValidationError or SandboxErrorCode.VerifierFailure &&
           ModuleAuditMatches(plan, auditEvent);

    private static bool VerifierFailureAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => !auditEvent.Success &&
           auditEvent.ErrorCode == SandboxErrorCode.VerifierFailure &&
           ModuleAuditMatches(plan, auditEvent);

    private static bool BindingAuditMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxAuditEvent auditEvent)
    {
        if (string.IsNullOrWhiteSpace(auditEvent.BindingId) ||
            !plan.BindingReferences.TryGetValue(entrypoint, out var entrypointBindings) ||
            !entrypointBindings.Contains(auditEvent.BindingId) ||
            !plan.Bindings.TryGet(auditEvent.BindingId, out var binding) ||
            binding.AuditLevel is AuditLevel.None or AuditLevel.Summary ||
            string.IsNullOrWhiteSpace(auditEvent.ResourceId) ||
            !CapabilityMatches(auditEvent, binding) ||
            !EffectMatches(auditEvent, binding) ||
            !ResultMatches(auditEvent) ||
            !RequiredBindingFieldsMatch(plan, auditEvent))
        {
            return false;
        }

        return true;
    }

    private static bool CapabilityMatches(SandboxAuditEvent auditEvent, BindingSignature binding)
        => binding.RequiredCapability is null ||
           string.Equals(auditEvent.CapabilityId, binding.RequiredCapability, StringComparison.Ordinal);

    private static bool EffectMatches(SandboxAuditEvent auditEvent, BindingSignature binding)
    {
        if (auditEvent.Effect == SandboxEffect.None ||
            (auditEvent.Effect & ~binding.Effects) != SandboxEffect.None)
        {
            return false;
        }

        var nonCpuEffects = binding.Effects & ~SandboxEffect.Cpu;
        return nonCpuEffects == SandboxEffect.None ||
               (auditEvent.Effect & nonCpuEffects) != SandboxEffect.None;
    }

    private static bool ResultMatches(SandboxAuditEvent auditEvent)
        => auditEvent.Success ? auditEvent.ErrorCode is null : auditEvent.ErrorCode is not null;

    private static bool RequiredBindingFieldsMatch(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (auditEvent.Fields is null ||
            !auditEvent.Fields.TryGetValue("resourceKind", out var resourceKind) ||
            string.IsNullOrWhiteSpace(resourceKind) ||
            !auditEvent.Fields.TryGetValue("durationMs", out var durationMs) ||
            !auditEvent.Fields.TryGetValue("moduleHash", out var moduleHash) ||
            !string.Equals(moduleHash, plan.ModuleHash, StringComparison.Ordinal) ||
            !auditEvent.Fields.TryGetValue("policyHash", out var policyHash) ||
            !string.Equals(policyHash, plan.PolicyHash, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var field in auditEvent.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) ||
                !TextIsSafe(field.Key) ||
                !TextIsSafe(field.Value))
            {
                return false;
            }
        }

        return double.TryParse(
                durationMs,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedDuration) &&
            parsedDuration >= 0;
    }

    private static bool TextIsSafe(string? value)
        => value is null ||
           string.Equals(AuditTextSanitizer.SanitizeAndRedact(value), value, StringComparison.Ordinal);
}
