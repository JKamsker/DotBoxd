namespace DotBoxD.Hosting;

using System.Diagnostics;
using DotBoxD.Kernels;

public sealed partial class SandboxHost
{
    internal ValueTask<PreparedExecutionResult> ExecutePreparedValueInProcessAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        Debug.Assert(options.Isolation == SandboxIsolation.InProcess);
        Debug.Assert(Enum.IsDefined(options.Mode));

        ThrowIfDisposed();
        if (TryGetRevokedCapability(plan, entrypoint, out var revoked))
        {
            return PublishedResult(CapabilityRevokedResult(plan, options, revoked));
        }

        if (options.RequireDeterministic && !plan.Policy.Deterministic)
        {
            return PublishedResult(DeterminismRequiredResult(plan, options));
        }

        return CanTryCompiledNoAuditValue(plan, entrypoint, options)
            ? ExecuteCompiledPreparedValueAsync(plan, entrypoint, input, options, cancellationToken)
            : ExecutePreparedResultAsValueAsync(plan, entrypoint, input, options, cancellationToken);
    }

    private async ValueTask<PreparedExecutionResult> ExecuteCompiledPreparedValueAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var executable = await _compiled.GetAsync(plan, entrypoint, cancellationToken).ConfigureAwait(false);
            if (!CompiledExecutionRunner.CanUseNoAuditSuccessPath(
                    plan,
                    entrypoint,
                    executable.Artifact,
                    options,
                    out var allowedBindings))
            {
                var fullResult = await CompiledExecutionRunner.ExecuteAsync(
                        executable,
                        plan,
                        entrypoint,
                        input,
                        options,
                        cancellationToken)
                    .ConfigureAwait(false);
                return PreparedExecutionResult.FromResult(Publish(fullResult));
            }

            var result = CompiledNoAuditValueRunner.Execute(
                executable,
                plan,
                entrypoint,
                input,
                options,
                allowedBindings,
                cancellationToken);
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
