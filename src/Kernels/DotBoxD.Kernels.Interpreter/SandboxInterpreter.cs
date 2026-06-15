namespace DotBoxD.Kernels.Interpreter;

using DotBoxD.Kernels;

public sealed class SandboxInterpreter : ISandboxInterpreter
{
    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
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
            var evaluator = new InterpreterEvaluator(plan, context, options);
            var value = await evaluator.ExecuteEntrypointAsync(entrypoint, input).ConfigureAwait(false);
            if (!options.SuppressSuccessfulRunSummaryAudit)
            {
                WriteSummary(audit, runId, startedAt, plan, budget, true, null);
            }

            return Result(plan, budget, audit, true, value, null);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            WriteSummary(audit, runId, startedAt, plan, budget, false, error);
            return Result(plan, budget, audit, false, null, error);
        }
        catch (SandboxRuntimeException ex)
        {
            WriteSummary(audit, runId, startedAt, plan, budget, false, ex.Error);
            return Result(plan, budget, audit, false, null, ex.Error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "sandbox execution failed");
            WriteSummary(audit, runId, startedAt, plan, budget, false, error);
            return Result(plan, budget, audit, false, null, error);
        }
    }

    private static SandboxExecutionResult Result(
        ExecutionPlan plan,
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
            AuditEvents = audit.SnapshotEvents(),
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };

    private static void WriteSummary(
        InMemoryAuditSink audit,
        SandboxRunId runId,
        DateTimeOffset startedAt,
        ExecutionPlan plan,
        ResourceMeter budget,
        bool success,
        SandboxError? error)
    {
        var fields = RunSummaryAuditFields.Create(plan, budget, ExecutionMode.Interpreted, "None");
        audit.Write(new SandboxAuditEvent(
            runId,
            "RunSummary",
            startedAt,
            success,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error?.Code,
            Message: $"mode=interpreted cacheStatus=None plan={plan.PlanHash} " +
                     $"policy={plan.PolicyHash} policyId={fields["policyId"]} bindings={plan.BindingManifestHash} " +
                     $"fuel={budget.FuelUsed}/{budget.Limits.MaxFuel}",
            Fields: fields));
    }

    private static DateTimeOffset AuditTime(ExecutionPlan plan)
        => plan.Policy.Deterministic
            ? plan.Policy.LogicalNow ?? DateTimeOffset.UnixEpoch
            : DateTimeOffset.UtcNow;
}
