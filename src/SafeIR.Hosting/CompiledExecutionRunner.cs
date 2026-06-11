namespace SafeIR.Hosting;

using SafeIR;
using SafeIR.Compiler;

internal static class CompiledExecutionRunner
{
    public static ValueTask<SandboxExecutionResult> ExecuteAsync(
        CompiledArtifact artifact,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var audit = new InMemoryAuditSink();
        var budget = new ResourceMeter(plan.Budget);
        var context = new SandboxContext(runId, plan.Policy, budget, plan.Bindings, audit, cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;

        try {
            context.ChargeValue(input);
            var value = artifact.Entrypoint(context, input);
            WriteSummary(audit, runId, startedAt, plan, artifact, budget, true, null);
            return ValueTask.FromResult(Result(plan, artifact, budget, audit, true, value, null));
        }
        catch (OperationCanceledException) {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            WriteSummary(audit, runId, startedAt, plan, artifact, budget, false, error);
            return ValueTask.FromResult(Result(plan, artifact, budget, audit, false, null, error));
        }
        catch (SandboxRuntimeException ex) {
            WriteSummary(audit, runId, startedAt, plan, artifact, budget, false, ex.Error);
            return ValueTask.FromResult(Result(plan, artifact, budget, audit, false, null, ex.Error));
        }
        catch (Exception) {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled sandbox execution failed");
            WriteSummary(audit, runId, startedAt, plan, artifact, budget, false, error);
            return ValueTask.FromResult(Result(plan, artifact, budget, audit, false, null, error));
        }
    }

    private static SandboxExecutionResult Result(
        ExecutionPlan plan,
        CompiledArtifact artifact,
        ResourceMeter budget,
        InMemoryAuditSink audit,
        bool succeeded,
        SandboxValue? value,
        SandboxError? error)
        => new() {
            Succeeded = succeeded,
            Value = value,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ExecutionMode.Compiled,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash,
            ArtifactHash = artifact.ArtifactHash
        };

    private static void WriteSummary(
        InMemoryAuditSink audit,
        SandboxRunId runId,
        DateTimeOffset startedAt,
        ExecutionPlan plan,
        CompiledArtifact artifact,
        ResourceMeter budget,
        bool success,
        SandboxError? error)
        => audit.Write(new SandboxAuditEvent(
            runId,
            "RunSummary",
            startedAt,
            success,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error?.Code,
            Message: $"mode=compiled runtimeForm={artifact.RuntimeForm} cacheStatus={artifact.CacheStatus} " +
                     $"cacheKey={artifact.Manifest.CacheKey} artifact={artifact.ArtifactHash} " +
                     $"plan={plan.PlanHash} policy={plan.PolicyHash} bindings={plan.BindingManifestHash} " +
                     $"fuel={budget.FuelUsed}/{budget.Limits.MaxFuel}"));
}
