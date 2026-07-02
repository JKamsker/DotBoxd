using DotBoxD.Hosting.Worker;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal sealed partial class SandboxWorkerExecutor
{
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
        if (!WorkerPayloadMatches(plan, entrypoint, result, out var resultShapeUsage))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker resource usage was malformed");
        if (!WorkerResourceUsageMatches(plan, result, resultShapeUsage))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker audit envelope was malformed");
        return WorkerAuditMatches(plan, entrypoint, options, result);
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

        return result.Succeeded
            ? WorkerRunSummaryValidator.IsHexSha256(result.ArtifactHash)
            : string.IsNullOrWhiteSpace(result.ArtifactHash) || WorkerRunSummaryValidator.IsHexSha256(result.ArtifactHash);
    }

    private static bool WorkerPayloadMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionResult result,
        out SandboxResourceUsage? resultShapeUsage)
    {
        resultShapeUsage = null;
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
                resultShapeUsage = WorkerResultShapeUsage.Measure(result.Value, analysis.ReturnType, plan.Budget);
            }
            catch (SandboxRuntimeException)
            {
                return false;
            }

            return true;
        }

        return result.Value is null &&
               WorkerEnvelopeValidators.ErrorMatches(result.Error);
    }

    private static bool WorkerResourceUsageMatches(
        ExecutionPlan plan,
        SandboxExecutionResult result,
        SandboxResourceUsage? resultShapeUsage)
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
               usage.StringBytes <= plan.Budget.MaxTotalStringBytes &&
               WorkerResultShapeUsageMatches(usage, resultShapeUsage);
    }

    private static bool WorkerResultShapeUsageMatches(
        SandboxResourceUsage usage,
        SandboxResourceUsage? resultShapeUsage)
        => resultShapeUsage is not { } shape ||
           (usage.FuelUsed >= shape.FuelUsed &&
            usage.AllocatedBytes >= shape.AllocatedBytes &&
            usage.CollectionElements >= shape.CollectionElements &&
            usage.StringBytes >= shape.StringBytes);

    private static bool WorkerAuditMatches(
        ExecutionPlan plan,
        string entrypoint,
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

        // Single pass over the audit envelope: enforce a common run id, run per-event
        // safety/schema validation, and capture the one required run summary without
        // allocating an intermediate summary array.
        SandboxAuditEvent? summary = null;
        var summaryCount = 0;
        var observedHostCalls = 0;
        var observedLogEvents = 0;
        var observedBindingBaseFuel = 0L;
        Dictionary<string, int>? observedBindingCalls = null;
        var expectedSequenceNumber = 1L;
        foreach (var auditEvent in result.AuditEvents)
        {
            if (auditEvent.SequenceNumber != expectedSequenceNumber ||
                auditEvent.RunId != runId ||
                !WorkerAuditValidator.Matches(plan, entrypoint, options, auditEvent))
            {
                return false;
            }
            expectedSequenceNumber++;

            if (IsBindingAudit(auditEvent.Kind))
            {
                observedHostCalls++;
                observedBindingCalls ??= new Dictionary<string, int>(StringComparer.Ordinal);
                if (!TryRecordBindingAuditEvidence(
                    plan,
                    auditEvent,
                    observedBindingCalls,
                    ref observedBindingBaseFuel))
                {
                    return false;
                }
            }

            if (auditEvent.Kind == "SandboxLog")
            {
                observedLogEvents++;
            }

            if (auditEvent.Kind == "RunSummary")
            {
                summaryCount++;
                if (summaryCount > 1)
                {
                    return false;
                }

                summary = auditEvent;
            }
        }

        if (summaryCount != 1 ||
            summary!.Success != result.Succeeded ||
            !AuditEvidenceUsageMatches(
                result.ResourceUsage,
                observedHostCalls,
                observedLogEvents,
                observedBindingBaseFuel))
        {
            return false;
        }

        return WorkerRunSummaryValidator.RunSummaryMatches(plan, result, summary) &&
            (result.Succeeded
            ? summary.ErrorCode is null
            : summary.ErrorCode == result.Error!.Code &&
              summary.ErrorCode is { } code &&
              Enum.IsDefined(code));
    }

    private static bool IsBindingAudit(string kind)
        => kind is "BindingCall" or "SandboxLog" or "PluginMessage";

    private static bool TryRecordBindingAuditEvidence(
        ExecutionPlan plan,
        SandboxAuditEvent auditEvent,
        Dictionary<string, int> observedBindingCalls,
        ref long observedBindingBaseFuel)
    {
        if (auditEvent.BindingId is null ||
            !plan.Bindings.TryGet(auditEvent.BindingId, out var binding))
        {
            return false;
        }

        try
        {
            observedBindingBaseFuel = checked(observedBindingBaseFuel + binding.CostModel.BaseFuel);
        }
        catch (OverflowException)
        {
            return false;
        }

        var calls = observedBindingCalls.TryGetValue(auditEvent.BindingId, out var existing)
            ? existing + 1
            : 1;
        observedBindingCalls[auditEvent.BindingId] = calls;
        return binding.CostModel.MaxCallsPerRun is not { } maxCalls ||
            calls <= maxCalls;
    }

    private static bool AuditEvidenceUsageMatches(
        SandboxResourceUsage usage,
        int observedHostCalls,
        int observedLogEvents,
        long observedBindingBaseFuel)
        => usage.HostCalls >= observedHostCalls &&
           usage.LogEvents >= observedLogEvents &&
           usage.FuelUsed >= observedBindingBaseFuel;
}
