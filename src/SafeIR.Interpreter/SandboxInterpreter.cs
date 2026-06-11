namespace SafeIR.Interpreter;

using SafeIR;

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
        var context = new SandboxContext(runId, plan.Policy, budget, plan.Bindings, audit, cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;

        try {
            var evaluator = new InterpreterEvaluator(plan, context, options);
            var value = await evaluator.ExecuteEntrypointAsync(entrypoint, input).ConfigureAwait(false);
            WriteSummary(audit, runId, startedAt, plan, budget, true, null);
            return Result(plan, budget, audit, true, value, null);
        }
        catch (OperationCanceledException) {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            WriteSummary(audit, runId, startedAt, plan, budget, false, error);
            return Result(plan, budget, audit, false, null, error);
        }
        catch (SandboxRuntimeException ex) {
            WriteSummary(audit, runId, startedAt, plan, budget, false, ex.Error);
            return Result(plan, budget, audit, false, null, ex.Error);
        }
        catch (Exception) {
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
        => new() {
            Succeeded = succeeded,
            Value = value,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ExecutionMode.Interpreted,
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
        => audit.Write(new SandboxAuditEvent(
            runId,
            "RunSummary",
            startedAt,
            success,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error?.Code,
            Message: $"mode=interpreted cacheStatus=None plan={plan.PlanHash} " +
                     $"policy={plan.PolicyHash} bindings={plan.BindingManifestHash} " +
                     $"fuel={budget.FuelUsed}/{budget.Limits.MaxFuel}"));
}
