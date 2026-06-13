namespace SafeIR.Hosting;

using System.Collections.ObjectModel;

using SafeIR;

public sealed partial class SandboxHost
{
    private static SandboxAuditEvent FallbackAudit(ExecutionPlan plan, SandboxRunId runId, SandboxError reason)
        => new(
            runId,
            "ExecutionFallback",
            AuditTime(plan),
            true,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: reason.Code,
            Message: $"compiled execution fell back to interpreted mode: {reason.SafeMessage}");

    private static IEnumerable<SandboxAuditEvent> FallbackSecurityAudits(
        ExecutionPlan plan,
        SandboxRunId runId,
        SandboxError reason)
    {
        if (reason.Code == SandboxErrorCode.VerifierFailure)
        {
            yield return VerifierFailureAudit(plan, runId, reason);
        }
    }

    private static SandboxExecutionResult CompilerUnavailableResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxError? reason = null)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var error = reason ?? new SandboxError(SandboxErrorCode.ValidationError, "compiled execution is not available for this run");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CompilerUnavailable",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, ExecutionMode.Compiled, error, false);

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.OwnedEventSnapshot(),
            ActualMode = ExecutionMode.Compiled,
            ExecutionDispatched = false,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult InvalidExecutionOptionsResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        string message)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var error = new SandboxError(SandboxErrorCode.ValidationError, message);
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "InvalidExecutionOptions",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
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

    private static SandboxExecutionResult CompiledFailureResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxError error)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CompiledExecutionFailed",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
        if (error.Code == SandboxErrorCode.VerifierFailure)
        {
            audit.Write(VerifierFailureAudit(plan, runId, error));
        }

        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, ExecutionMode.Compiled, error, false);

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.OwnedEventSnapshot(),
            ActualMode = ExecutionMode.Compiled,
            ExecutionDispatched = false,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult DeterminismRequiredResult(ExecutionPlan plan, SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var error = new SandboxError(SandboxErrorCode.PolicyDenied, "deterministic execution is required");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "PolicyDenied",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
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

    private static SandboxExecutionResult CapabilityRevokedResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        RevokedCapability revoked)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var error = new SandboxError(
            SandboxErrorCode.PolicyDenied,
            $"capability {revoked.Id} has been revoked");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CapabilityRevoked",
            startedAt,
            false,
            CapabilityId: revoked.Id,
            ResourceId: $"capability:{revoked.Id}",
            ErrorCode: error.Code,
            Message: revoked.Reason,
            Fields: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["capabilityId"] = revoked.Id,
                ["reason"] = revoked.Reason,
                ["revokedAt"] = revoked.RevokedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
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

    internal static SandboxExecutionResult WorkerIsolationUnavailableResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxWorkerProfile? profile)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var error = new SandboxError(
            SandboxErrorCode.PolicyDenied,
            profile is null
                ? "worker process isolation is not configured"
                : "worker process isolation profile is incomplete");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "WorkerIsolationUnavailable",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage,
            Fields: profile?.ToAuditFields()));
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

    internal static SandboxExecutionResult WorkerIsolationFailedResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxError error)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "WorkerIsolationFailed",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
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

    private static SandboxAuditEvent VerifierFailureAudit(
        ExecutionPlan plan,
        SandboxRunId runId,
        SandboxError error)
        => new(
            runId,
            "VerifierFailure",
            AuditTime(plan),
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage);

    private static void WriteFailedRunSummary(
        InMemoryAuditSink audit,
        SandboxRunId runId,
        DateTimeOffset startedAt,
        ExecutionPlan plan,
        ResourceMeter budget,
        ExecutionMode mode,
        SandboxError error,
        bool executionDispatched)
    {
        var cacheStatus = "None";
        var fields = RunSummaryAuditFields.Create(
            plan,
            budget,
            mode,
            cacheStatus,
            executionDispatched: executionDispatched);
        audit.Write(new SandboxAuditEvent(
            runId,
            "RunSummary",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: $"mode={mode.ToString().ToLowerInvariant()} cacheStatus={cacheStatus} " +
                     $"plan={plan.PlanHash} policy={plan.PolicyHash} policyId={fields["policyId"]} " +
                     $"bindings={plan.BindingManifestHash} fuel={budget.FuelUsed}/{budget.Limits.MaxFuel}",
            Fields: fields));
    }

    private static DateTimeOffset AuditTime(ExecutionPlan plan)
        => plan.Policy.Deterministic
            ? plan.Policy.LogicalNow ?? DateTimeOffset.UnixEpoch
            : DateTimeOffset.UtcNow;
}

internal static class SandboxAuditEventSequence
{
    public static IReadOnlyList<SandboxAuditEvent> ToSequencedArray(this IEnumerable<SandboxAuditEvent> events)
    {
        var sink = new InMemoryAuditSink();
        foreach (var auditEvent in events)
        {
            sink.Write(auditEvent);
        }

        return sink.OwnedEventSnapshot();
    }

    /// <summary>
    /// Wraps the sink's already-fresh event array in a read-only collection without copying
    /// it again, producing an owned immutable snapshot that result construction can adopt.
    /// </summary>
    internal static IReadOnlyList<SandboxAuditEvent> OwnedEventSnapshot(this InMemoryAuditSink sink)
        => new ReadOnlyCollection<SandboxAuditEvent>((IList<SandboxAuditEvent>)sink.Events);
}
