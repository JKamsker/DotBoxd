using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
namespace DotBoxD.Hosting.Execution;
internal static class CompiledExecutionRunner
{
    public static ValueTask<SandboxExecutionResult> ExecuteAsync(
        CompiledExecutable executable,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        bool useInlineAwaitPump = false)
    {
        var artifact = executable.Artifact;
        if (CanUseNoAuditSuccessPath(plan, entrypoint, artifact, options, out var noAuditBindings))
        {
            return ExecuteNoAuditSuccessAsync(
                executable,
                plan,
                entrypoint,
                input,
                options,
                noAuditBindings,
                cancellationToken);
        }
        var result = useInlineAwaitPump
            ? CompiledAsyncWorker.RunInline(() => ExecuteCore(executable, plan, entrypoint, input, options, cancellationToken))
            : ExecuteCore(executable, plan, entrypoint, input, options, cancellationToken);
        return ValueTask.FromResult(result);
    }
    public static ValueTask<SandboxExecutionResult> ExecuteOnWorkerAsync(
        CompiledExecutable executable,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
        => CompiledAsyncWorker.RunAsync(
            () => ExecuteCore(executable, plan, entrypoint, input, options, cancellationToken));
    private static SandboxExecutionResult ExecuteCore(
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
        plan.BindingReferences.TryGetValue(entrypoint, out var allowedBindings);
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
            if (!options.SuppressSuccessfulRunSummaryAudit)
            {
                WriteSummary(audit, runId, startedAt, plan, executable, budget, true, null);
            }

            return Result(plan, artifact, budget, audit, true, value, null);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            WriteSummary(audit, runId, startedAt, plan, executable, budget, false, error);
            return Result(plan, artifact, budget, audit, false, null, error);
        }
        catch (SandboxRuntimeException ex)
        {
            WriteSummary(audit, runId, startedAt, plan, executable, budget, false, ex.Error);
            return Result(plan, artifact, budget, audit, false, null, ex.Error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled sandbox execution failed");
            WriteSummary(audit, runId, startedAt, plan, executable, budget, false, error);
            return Result(plan, artifact, budget, audit, false, null, error);
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
            AuditEvents = audit.OwnedEventSnapshot(),
            ActualMode = ExecutionMode.Compiled,
            ExecutionDispatched = true,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash,
            ArtifactHash = artifact.ArtifactHash
        };

    private static ValueTask<SandboxExecutionResult> ExecuteNoAuditSuccessAsync(
        CompiledExecutable executable,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        IReadOnlySet<string> allowedBindings,
        CancellationToken cancellationToken)
    {
        var artifact = executable.Artifact;
        var budget = new ResourceMeter(plan.Budget);
        var context = new SandboxContext(
            SandboxRunId.Suppressed,
            plan.Policy,
            budget,
            plan.Bindings,
            NoopAuditSink.Instance,
            cancellationToken,
            allowedBindings,
            plan.ModuleHash,
            plan.PolicyHash);

        try
        {
            budget.CheckDeadline();
            context.ChargeValue(input);
            var value = artifact.Entrypoint(context, input);
            EnsureReturnType(plan, entrypoint, value);
            return ValueTask.FromResult(NoAuditSuccessResult(plan, artifact, budget, value));
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            return ValueTask.FromResult(FailureResult(plan, executable, options, budget, error));
        }
        catch (SandboxRuntimeException ex)
        {
            return ValueTask.FromResult(FailureResult(plan, executable, options, budget, ex.Error));
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled sandbox execution failed");
            return ValueTask.FromResult(FailureResult(plan, executable, options, budget, error));
        }
    }

    private static SandboxExecutionResult NoAuditSuccessResult(
        ExecutionPlan plan,
        CompiledArtifact artifact,
        ResourceMeter budget,
        SandboxValue value)
        => new()
        {
            Succeeded = true,
            Value = value,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = InMemoryAuditSink.EmptyEventSnapshot,
            ActualMode = ExecutionMode.Compiled,
            ExecutionDispatched = true,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash,
            ArtifactHash = artifact.ArtifactHash
        };

    internal static SandboxExecutionResult FailureResult(
        ExecutionPlan plan,
        CompiledExecutable executable,
        SandboxExecutionOptions options,
        ResourceMeter budget,
        SandboxError error)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var audit = new InMemoryAuditSink();
        WriteSummary(audit, runId, AuditTime(plan), plan, executable, budget, false, error);
        return Result(plan, executable.Artifact, budget, audit, false, null, error);
    }

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
        var fields = RunSummaryAuditFields.Create(
            plan,
            budget,
            ExecutionMode.Compiled,
            cacheStatus,
            artifact.RuntimeForm.ToString(),
            artifact.Manifest.CacheKey,
            artifact.ArtifactHash,
            executable.MaterializationStatus);
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
                     $"plan={plan.PlanHash} policy={plan.PolicyHash} policyId={fields["policyId"]} bindings={plan.BindingManifestHash} " +
                     $"fuel={budget.FuelUsed}/{budget.Limits.MaxFuel}",
            Fields: fields));
    }

    internal static bool CanUseNoAuditSuccessPath(
        ExecutionPlan plan,
        string entrypoint,
        CompiledArtifact artifact,
        SandboxExecutionOptions options,
        out IReadOnlySet<string> allowedBindings)
    {
        if (options.SuppressSuccessfulRunSummaryAudit &&
            artifact.CacheInvalidReason is null &&
            plan.BindingReferences.TryGetValue(entrypoint, out allowedBindings!) &&
            allowedBindings.Count == 0)
        {
            return true;
        }

        allowedBindings = default!;
        return false;
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

    internal static void EnsureReturnType(ExecutionPlan plan, string entrypoint, SandboxValue? value)
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
