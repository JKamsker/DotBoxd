namespace DotBoxD.Hosting;

using System.Diagnostics;
using DotBoxD.Kernels;

public sealed partial class SandboxHost
{
    internal ValueTask<SandboxExecutionResult> ExecutePreparedInProcessAsync(
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
            return ValueTask.FromResult(Publish(CapabilityRevokedResult(plan, options, revoked)));
        }

        if (options.RequireDeterministic && !plan.Policy.Deterministic)
        {
            return ValueTask.FromResult(Publish(DeterminismRequiredResult(plan, options)));
        }

        var execution = options.Mode switch
        {
            ExecutionMode.Compiled => ExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken),
            ExecutionMode.Interpreted => ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken),
            ExecutionMode.Auto => ExecuteAutoAsync(plan, entrypoint, input, options, cancellationToken),
            _ => ValueTask.FromResult(CompilerUnavailableResult(plan, options))
        };
        return PublishAsync(execution);
    }

    private ValueTask<SandboxExecutionResult> PublishAsync(ValueTask<SandboxExecutionResult> execution)
        => execution.IsCompletedSuccessfully
            ? ValueTask.FromResult(Publish(execution.Result))
            : AwaitAndPublishAsync(execution);

    private async ValueTask<SandboxExecutionResult> AwaitAndPublishAsync(
        ValueTask<SandboxExecutionResult> execution)
        => Publish(await execution.ConfigureAwait(false));
}
