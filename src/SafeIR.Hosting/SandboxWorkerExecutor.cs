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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(plan.Budget.EffectiveWallTime);
        try
        {
            var result = await worker.Client.ExecuteInWorkerAsync(
                    plan,
                    entrypoint,
                    input,
                    workerOptions,
                    timeout.Token)
                .ConfigureAwait(false);
            return ValidateWorkerResult(plan, entrypoint, options, result, out var error)
                ? result with
                {
                    AuditEvents = result.AuditEvents.ToSequencedArray(),
                    ExecutionDispatched = true
                }
                : SandboxHost.WorkerIsolationFailedResult(
                    plan,
                    options,
                    error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.Cancelled, "worker process execution was cancelled"));
        }
        catch (OperationCanceledException)
        {
            return SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.Timeout, "worker process execution timed out"));
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

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker resource usage was malformed");
        if (!WorkerResourceUsageMatches(plan, result))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker audit envelope was malformed");
        return WorkerAuditMatches(plan, options, result);
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

        return !result.Succeeded || IsHexSha256(result.ArtifactHash);
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

    private static bool WorkerResourceUsageMatches(ExecutionPlan plan, SandboxExecutionResult result)
    {
        var usage = result.ResourceUsage;
        return usage.MaxFuel == plan.Budget.MaxFuel &&
               usage.FuelUsed >= 0 &&
               usage.FuelUsed <= plan.Budget.MaxFuel &&
               usage.LoopIterations >= 0 &&
               usage.LoopIterations <= plan.Budget.MaxLoopIterations &&
               usage.AllocatedBytes >= 0 &&
               usage.AllocatedBytes <= plan.Budget.MaxAllocatedBytes &&
               usage.HostCalls >= 0 &&
               usage.HostCalls <= plan.Budget.MaxHostCalls &&
               usage.FileBytesRead >= 0 &&
               usage.FileBytesRead <= plan.Budget.MaxFileBytesRead &&
               usage.FileBytesWritten >= 0 &&
               usage.FileBytesWritten <= plan.Budget.MaxFileBytesWritten &&
               usage.NetworkBytesRead >= 0 &&
               usage.NetworkBytesRead <= plan.Budget.MaxNetworkBytesRead &&
               usage.NetworkBytesWritten >= 0 &&
               usage.NetworkBytesWritten <= plan.Budget.MaxNetworkBytesWritten &&
               usage.LogEvents >= 0 &&
               usage.LogEvents <= plan.Budget.MaxLogEvents &&
               usage.CollectionElements >= 0 &&
               usage.CollectionElements <= plan.Budget.MaxTotalCollectionElements &&
               usage.StringBytes >= 0 &&
               usage.StringBytes <= plan.Budget.MaxTotalStringBytes;
    }

    private static bool WorkerAuditMatches(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxExecutionResult result)
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

        return WorkerRunSummaryMatches(plan, result, summaries[0]) &&
            (result.Succeeded
            ? summaries[0].ErrorCode is null
            : summaries[0].ErrorCode == result.Error!.Code);
    }

    private static bool WorkerRunSummaryMatches(
        ExecutionPlan plan,
        SandboxExecutionResult result,
        SandboxAuditEvent summary)
    {
        if (summary.Fields is null ||
            !FieldEquals(summary, "mode", result.ActualMode.ToString()) ||
            !FieldEquals(summary, "executionDispatched", true) ||
            !HasNonEmptyField(summary, "cacheStatus") ||
            !FieldEquals(summary, "moduleHash", plan.ModuleHash) ||
            !FieldEquals(summary, "planHash", plan.PlanHash) ||
            !FieldEquals(summary, "policyHash", plan.PolicyHash) ||
            !FieldEquals(summary, "bindingManifestHash", plan.BindingManifestHash) ||
            !FieldEquals(summary, "fuelUsed", result.ResourceUsage.FuelUsed) ||
            !FieldEquals(summary, "maxFuel", result.ResourceUsage.MaxFuel) ||
            !FieldEquals(summary, "loopIterations", result.ResourceUsage.LoopIterations) ||
            !FieldEquals(summary, "allocatedBytes", result.ResourceUsage.AllocatedBytes) ||
            !FieldEquals(summary, "hostCalls", result.ResourceUsage.HostCalls) ||
            !FieldEquals(summary, "fileBytesRead", result.ResourceUsage.FileBytesRead) ||
            !FieldEquals(summary, "fileBytesWritten", result.ResourceUsage.FileBytesWritten) ||
            !FieldEquals(summary, "networkBytesRead", result.ResourceUsage.NetworkBytesRead) ||
            !FieldEquals(summary, "networkBytesWritten", result.ResourceUsage.NetworkBytesWritten) ||
            !FieldEquals(summary, "logEvents", result.ResourceUsage.LogEvents) ||
            !FieldEquals(summary, "collectionElements", result.ResourceUsage.CollectionElements) ||
            !FieldEquals(summary, "stringBytes", result.ResourceUsage.StringBytes))
        {
            return false;
        }

        if (result.ActualMode != ExecutionMode.Compiled)
        {
            return !summary.Fields.ContainsKey("artifactHash") &&
                   !summary.Fields.ContainsKey("runtimeForm") &&
                   !summary.Fields.ContainsKey("cacheKey");
        }

        if (!result.Succeeded)
        {
            return true;
        }

        if (!IsHexSha256(result.ArtifactHash))
        {
            return false;
        }

        var artifactHash = result.ArtifactHash!;
        return FieldEquals(summary, "artifactHash", artifactHash) &&
               FieldEquals(summary, "runtimeForm", "LoadedAssembly") &&
               HasHexSha256Field(summary, "cacheKey");
    }

    private static bool FieldEquals(SandboxAuditEvent summary, string key, string value)
        => summary.Fields!.TryGetValue(key, out var actual) &&
           string.Equals(actual, value, StringComparison.Ordinal);

    private static bool FieldEquals(SandboxAuditEvent summary, string key, long value)
        => FieldEquals(summary, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static bool FieldEquals(SandboxAuditEvent summary, string key, bool value)
        => FieldEquals(summary, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static bool HasNonEmptyField(SandboxAuditEvent summary, string key)
        => summary.Fields!.TryGetValue(key, out var value) &&
           !string.IsNullOrWhiteSpace(value);

    private static bool HasHexSha256Field(SandboxAuditEvent summary, string key)
        => summary.Fields!.TryGetValue(key, out var value) && IsHexSha256(value);

    private static bool IsHexSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
