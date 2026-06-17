using DotBoxD.Kernels;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost
{
    private static SandboxExecutionResult CapabilityDeniedResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        CapabilityDenial denial)
        => denial switch
        {
            RevokedCapabilityDenial revoked => CapabilityRevokedResult(plan, options, revoked.Revoked),
            UnavailableCapabilityDenial unavailable => CapabilityUnavailableResult(
                plan,
                options,
                unavailable.Unavailable),
            _ => throw new InvalidOperationException("unknown capability denial")
        };

    private static SandboxExecutionResult CapabilityUnavailableResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        UnavailableCapability unavailable)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var error = new SandboxError(
            SandboxErrorCode.PolicyDenied,
            $"capability {unavailable.Id} is not currently granted");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CapabilityUnavailable",
            startedAt,
            false,
            CapabilityId: unavailable.Id,
            ResourceId: $"capability:{unavailable.Id}",
            ErrorCode: error.Code,
            Message: error.SafeMessage,
            Fields: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["capabilityId"] = unavailable.Id,
                ["checkedAt"] = unavailable.CheckedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            }));
        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, options.Mode, error, false);

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.OwnedEventSnapshot(),
            ActualMode = options.Mode,
            ExecutionDispatched = false,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }
}
