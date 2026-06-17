using System.Diagnostics;
using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost
{
    internal ValueTask<PreparedExecutionResult> ExecutePreparedValueInProcessAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken = default,
        CompiledNoAuditRunState? reusableNoAuditState = null)
    {
        Debug.Assert(options.Isolation == SandboxIsolation.InProcess);
        Debug.Assert(Enum.IsDefined(options.Mode));

        ThrowIfDisposed();
        if (TryGetCapabilityDenial(plan, entrypoint, out var denial))
        {
            return PublishedResult(CapabilityDeniedResult(plan, options, denial));
        }

        if (options.RequireDeterministic && !plan.Policy.Deterministic)
        {
            return PublishedResult(DeterminismRequiredResult(plan, options));
        }

        return CanTryCompiledNoAuditValue(plan, entrypoint, options)
            ? ExecuteCompiledPreparedValueAsync(
                plan,
                entrypoint,
                input,
                options,
                cancellationToken,
                reusableNoAuditState)
            : ExecutePreparedResultAsValueAsync(plan, entrypoint, input, options, cancellationToken);
    }

    private async ValueTask<PreparedExecutionResult> ExecuteCompiledPreparedValueAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? reusableNoAuditState)
    {
        try
        {
            var executableCached = false;
            CompiledExecutable executable = default;
            if (reusableNoAuditState is not null)
            {
                executableCached = reusableNoAuditState.TryGetExecutable(entrypoint, out executable);
            }

            if (!executableCached)
            {
                executable = await _compiled.GetAsync(plan, entrypoint, cancellationToken).ConfigureAwait(false);
            }

            if (!CompiledExecutionRunner.CanUseNoAuditSuccessPath(
                    plan,
                    entrypoint,
                    executable.Artifact,
                    options,
                    out var allowedBindings))
            {
                var execution = ShouldUseCompiledAsyncWorker(plan, entrypoint)
                    ? CompiledExecutionRunner.ExecuteOnWorkerAsync(
                        executable, plan, entrypoint, input, options, cancellationToken)
                    : CompiledExecutionRunner.ExecuteAsync(
                        executable, plan, entrypoint, input, options, cancellationToken,
                        ShouldUseCompiledInlineAwaitPump(plan, entrypoint));
                var fullResult = await execution
                    .ConfigureAwait(false);
                return PreparedExecutionResult.FromResult(Publish(fullResult));
            }

            if (!executableCached)
            {
                reusableNoAuditState?.StoreExecutable(entrypoint, executable);
            }

            var result = CompiledNoAuditValueRunner.Execute(
                executable,
                plan,
                entrypoint,
                input,
                options,
                allowedBindings,
                cancellationToken,
                reusableNoAuditState);
            return result.FullResult is null
                ? result
                : PreparedExecutionResult.FromResult(Publish(result.FullResult));
        }
        catch (SandboxRuntimeException ex) when (CanFallback(options, ex))
        {
            var fallback = await ExecuteFallbackToInterpreterAsync(
                    plan,
                    entrypoint,
                    input,
                    options,
                    ex.Error,
                    cancellationToken)
                .ConfigureAwait(false);
            return PreparedExecutionResult.FromResult(Publish(fallback));
        }
        catch (SandboxRuntimeException ex)
        {
            return PreparedExecutionResult.FromResult(Publish(CompiledFailureResult(plan, options, ex.Error)));
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            return PreparedExecutionResult.FromResult(Publish(CompiledFailureResult(plan, options, error)));
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled execution failed");
            return PreparedExecutionResult.FromResult(Publish(CompiledFailureResult(plan, options, error)));
        }
    }

    private async ValueTask<PreparedExecutionResult> ExecutePreparedResultAsValueAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var result = await ExecutePreparedInProcessAsync(plan, entrypoint, input, options, cancellationToken)
            .ConfigureAwait(false);
        return PreparedExecutionResult.FromResult(result);
    }

    private bool CanTryCompiledNoAuditValue(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options)
        => _compiled.IsAvailable &&
           options.Mode == ExecutionMode.Compiled &&
           !options.EnableDebugTrace &&
           options.SuppressSuccessfulRunSummaryAudit &&
           plan.BindingReferences.TryGetValue(entrypoint, out var allowedBindings) &&
           allowedBindings.Count == 0;

    private ValueTask<PreparedExecutionResult> PublishedResult(SandboxExecutionResult result)
        => ValueTask.FromResult(PreparedExecutionResult.FromResult(Publish(result)));
}
