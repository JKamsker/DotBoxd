namespace SafeIR.Hosting;

using SafeIR;

internal sealed class SandboxWorkerExecutor(ConfiguredSandboxWorker? worker)
{
    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (worker is null || !worker.Profile.SatisfiesWorkerProcess)
        {
            return SandboxHost.WorkerIsolationUnavailableResult(plan, options, worker?.Profile);
        }

        var workerOptions = options with { Isolation = SandboxIsolation.InProcess };
        try
        {
            var result = await worker.Client.ExecuteInWorkerAsync(
                    plan,
                    entrypoint,
                    input,
                    workerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return ValidateWorkerResult(plan, entrypoint, options, result, out var error)
                ? result with { AuditEvents = result.AuditEvents.ToSequencedArray() }
                : SandboxHost.WorkerIsolationFailedResult(
                    plan,
                    options,
                    error);
        }
        catch (OperationCanceledException)
        {
            return SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.Cancelled, "worker process execution was cancelled"));
        }
        catch (Exception)
        {
            return SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.HostFailure, "worker process execution failed"));
        }
    }

    private static bool ValidateWorkerResult(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        out SandboxError error)
    {
        error = new SandboxError(SandboxErrorCode.HostFailure, "worker result identity did not match the requested plan");
        if (!string.Equals(result.ModuleHash, plan.ModuleHash, StringComparison.Ordinal) ||
            !string.Equals(result.PlanHash, plan.PlanHash, StringComparison.Ordinal) ||
            !string.Equals(result.PolicyHash, plan.PolicyHash, StringComparison.Ordinal))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker result mode did not match the requested execution mode");
        if (!WorkerModeMatches(options, result))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker result payload was malformed");
        if (!WorkerPayloadMatches(plan, entrypoint, result))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker audit envelope was malformed");
        return WorkerAuditMatches(options, result);
    }

    private static bool WorkerModeMatches(SandboxExecutionOptions options, SandboxExecutionResult result)
    {
        if (!Enum.IsDefined(result.ActualMode) || result.ActualMode == ExecutionMode.Auto)
        {
            return false;
        }

        if (options.Mode == ExecutionMode.Interpreted && result.ActualMode != ExecutionMode.Interpreted)
        {
            return false;
        }

        if (options.Mode == ExecutionMode.Compiled &&
            !options.AllowFallbackToInterpreter &&
            result.ActualMode != ExecutionMode.Compiled)
        {
            return false;
        }

        if (result.ActualMode == ExecutionMode.Interpreted)
        {
            return string.IsNullOrWhiteSpace(result.ArtifactHash);
        }

        return !result.Succeeded || !string.IsNullOrWhiteSpace(result.ArtifactHash);
    }

    private static bool WorkerPayloadMatches(ExecutionPlan plan, string entrypoint, SandboxExecutionResult result)
    {
        if (result.Succeeded)
        {
            if (result.Value is null || result.Error is not null)
            {
                return false;
            }

            if (!plan.FunctionAnalysis.TryGetValue(entrypoint, out var analysis))
            {
                return false;
            }

            try
            {
                EntrypointBinder.RequireType(result.Value, analysis.ReturnType, "worker result return type mismatch");
            }
            catch (SandboxRuntimeException)
            {
                return false;
            }

            return true;
        }

        return result.Value is null && result.Error is not null;
    }

    private static bool WorkerAuditMatches(SandboxExecutionOptions options, SandboxExecutionResult result)
    {
        if (result.AuditEvents.Count == 0)
        {
            return false;
        }

        var runId = result.AuditEvents[0].RunId;
        if (options.RunId is not null && options.RunId != runId)
        {
            return false;
        }

        if (result.AuditEvents.Any(e => e.RunId != runId))
        {
            return false;
        }

        var summaries = result.AuditEvents.Where(e => e.Kind == "RunSummary").ToArray();
        if (summaries.Length != 1 || summaries[0].Success != result.Succeeded)
        {
            return false;
        }

        return result.Succeeded
            ? summaries[0].ErrorCode is null
            : summaries[0].ErrorCode == result.Error!.Code;
    }
}
