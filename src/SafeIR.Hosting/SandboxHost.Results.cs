namespace SafeIR.Hosting;

using SafeIR;

public sealed partial class SandboxHost
{
    private static SandboxAuditEvent FallbackAudit(ExecutionPlan plan, SandboxRunId runId, SandboxError reason)
        => new(
            runId,
            "ExecutionFallback",
            DateTimeOffset.UtcNow,
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

    private static SandboxExecutionResult CompilerUnavailableResult(ExecutionPlan plan, SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = DateTimeOffset.UtcNow;
        var error = new SandboxError(SandboxErrorCode.ValidationError, "compiled execution is not available for this run");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CompilerUnavailable",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, ExecutionMode.Compiled, error);

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ExecutionMode.Compiled,
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
        var startedAt = DateTimeOffset.UtcNow;
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

        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, ExecutionMode.Compiled, error);

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ExecutionMode.Compiled,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult DeterminismRequiredResult(ExecutionPlan plan, SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = DateTimeOffset.UtcNow;
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
        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, options.Mode, error);

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = options.Mode,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult WorkerIsolationUnavailableResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = DateTimeOffset.UtcNow;
        var error = new SandboxError(
            SandboxErrorCode.PolicyDenied,
            "worker process isolation is not configured");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "WorkerIsolationUnavailable",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, options.Mode, error);

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = options.Mode,
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
            DateTimeOffset.UtcNow,
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
        SandboxError error)
    {
        var cacheStatus = "None";
        audit.Write(new SandboxAuditEvent(
            runId,
            "RunSummary",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: $"mode={mode.ToString().ToLowerInvariant()} cacheStatus={cacheStatus} " +
                     $"plan={plan.PlanHash} policy={plan.PolicyHash} " +
                     $"bindings={plan.BindingManifestHash} fuel={budget.FuelUsed}/{budget.Limits.MaxFuel}",
            Fields: RunSummaryAuditFields.Create(plan, budget, mode, cacheStatus)));
    }
}
