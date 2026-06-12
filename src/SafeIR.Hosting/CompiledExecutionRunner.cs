namespace SafeIR.Hosting;

using SafeIR;
using SafeIR.Compiler;

internal static class CompiledExecutionRunner
{
    public static ValueTask<SandboxExecutionResult> ExecuteAsync(
        CompiledExecutable executable,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var artifact = executable.Artifact;
        var runId = options.RunId ?? SandboxRunId.New();
        var audit = new InMemoryAuditSink();
        var budget = new ResourceMeter(plan.Budget);
        var allowedBindings = BindingReferenceCollector.Collect(plan.Module, plan.Bindings, entrypoint);
        var context = new SandboxContext(
            runId,
            plan.Policy,
            budget,
            plan.Bindings,
            audit,
            cancellationToken,
            allowedBindings,
            plan.ModuleHash,
            plan.PolicyHash);
        var startedAt = AuditTime(plan);

        try
        {
            budget.CheckDeadline();
            context.ChargeValue(input);
            WriteCacheInvalidated(audit, runId, startedAt, plan, artifact);
            var value = artifact.Entrypoint(context, input);
            EnsureReturnType(plan, entrypoint, value);
            WriteSummary(audit, runId, startedAt, plan, executable, budget, true, null);
            return ValueTask.FromResult(Result(plan, artifact, budget, audit, true, value, null));
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            WriteSummary(audit, runId, startedAt, plan, executable, budget, false, error);
            return ValueTask.FromResult(Result(plan, artifact, budget, audit, false, null, error));
        }
        catch (SandboxRuntimeException ex)
        {
            WriteSummary(audit, runId, startedAt, plan, executable, budget, false, ex.Error);
            return ValueTask.FromResult(Result(plan, artifact, budget, audit, false, null, ex.Error));
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled sandbox execution failed");
            WriteSummary(audit, runId, startedAt, plan, executable, budget, false, error);
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
        => new()
        {
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
        CompiledExecutable executable,
        ResourceMeter budget,
        bool success,
        SandboxError? error)
    {
        var artifact = executable.Artifact;
        var cacheStatus = artifact.CacheStatus.ToString();
        audit.Write(new SandboxAuditEvent(
            runId,
            "RunSummary",
            startedAt,
            success,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error?.Code,
            Message: $"mode=compiled runtimeForm={artifact.RuntimeForm} cacheStatus={cacheStatus} " +
                     $"materializationStatus={executable.MaterializationStatus} " +
                     $"cacheKey={artifact.Manifest.CacheKey} artifact={artifact.ArtifactHash} " +
                     $"plan={plan.PlanHash} policy={plan.PolicyHash} policyId={plan.Policy.PolicyId} bindings={plan.BindingManifestHash} " +
                     $"fuel={budget.FuelUsed}/{budget.Limits.MaxFuel}",
            Fields: RunSummaryAuditFields.Create(
                plan,
                budget,
                ExecutionMode.Compiled,
                cacheStatus,
                artifact.RuntimeForm.ToString(),
                artifact.Manifest.CacheKey,
                artifact.ArtifactHash,
                executable.MaterializationStatus)));
    }

    private static void WriteCacheInvalidated(
        InMemoryAuditSink audit,
        SandboxRunId runId,
        DateTimeOffset startedAt,
        ExecutionPlan plan,
        CompiledArtifact artifact)
    {
        if (artifact.CacheInvalidReason is null)
        {
            return;
        }

        audit.Write(new SandboxAuditEvent(
            runId,
            "CacheInvalidated",
            startedAt,
            false,
            ResourceId: $"cache:{artifact.Manifest.CacheKey}",
            ErrorCode: SandboxErrorCode.CacheInvalid,
            Message: "compiled cache entry was quarantined and regenerated",
            Fields: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cacheKey"] = artifact.Manifest.CacheKey,
                ["moduleHash"] = plan.ModuleHash,
                ["planHash"] = plan.PlanHash,
                ["reason"] = artifact.CacheInvalidReason
            }));
    }

    private static void EnsureReturnType(ExecutionPlan plan, string entrypoint, SandboxValue? value)
    {
        if (value is null || !plan.FunctionAnalysis.TryGetValue(entrypoint, out var analysis))
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "function return type mismatch"));
        }

        EntrypointBinder.RequireType(value, analysis.ReturnType, "function return type mismatch");
    }

    private static DateTimeOffset AuditTime(ExecutionPlan plan)
        => plan.Policy.Deterministic
            ? plan.Policy.LogicalNow ?? DateTimeOffset.UnixEpoch
            : DateTimeOffset.UtcNow;
}
