using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal sealed partial class SandboxWorkerExecutor(ConfiguredSandboxWorker? worker)
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
            return Execution.SandboxHost.WorkerIsolationUnavailableResult(plan, options, worker?.Profile);
        }

        // SuppressSuccessfulRunSummaryAudit is an in-process allocation optimization only.
        // Worker-result validation (WorkerAuditMatches) structurally requires exactly one
        // RunSummary, so the worker must always emit it; suppressing it here would make every
        // successful worker run fail audit validation. Clearing the flag keeps the worker on
        // the canonical full-audit envelope and lets the validator stay strict.
        var workerOptions = options with
        {
            Isolation = SandboxIsolation.InProcess,
            SuppressSuccessfulRunSummaryAudit = false
        };
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
            if (timeout.IsCancellationRequested)
            {
                return Execution.SandboxHost.WorkerIsolationFailedResult(
                    plan,
                    options,
                    WorkerCancellationOrTimeoutError(cancellationToken));
            }

            return ValidateWorkerResult(plan, entrypoint, options, result, out var error)
                ? result with
                {
                    AuditEvents = result.AuditEvents.ToSequencedArray(),
                    ExecutionDispatched = true
                }
                : Execution.SandboxHost.WorkerIsolationFailedResult(
                    plan,
                    options,
                    error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Execution.SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                WorkerCancellationOrTimeoutError(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            return Execution.SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                WorkerCancellationOrTimeoutError(cancellationToken));
        }
        catch (Exception)
        {
            return Execution.SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.HostFailure, "worker process execution failed"));
        }
    }

    private static SandboxError WorkerCancellationOrTimeoutError(CancellationToken callerToken)
        => callerToken.IsCancellationRequested
            ? new SandboxError(SandboxErrorCode.Cancelled, "worker process execution was cancelled")
            : new SandboxError(SandboxErrorCode.Timeout, "worker process execution timed out");
}
