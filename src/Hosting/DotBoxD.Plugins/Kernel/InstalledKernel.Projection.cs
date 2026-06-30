using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Kernel;

/// <summary>
/// Result of running a lowered local-terminal chain for one event: whether the lowered <c>ShouldHandle</c>
/// (the <c>Where</c> filter) matched, and if so the value produced by the lowered <c>Handle</c> entrypoint
/// (the <c>Select</c> projection). Used by the server-side push path to forward only filtered, projected
/// values across the IPC boundary to the plugin's native <c>RunLocal</c> delegate.
/// </summary>
internal readonly record struct ProjectionResult(bool Matched, SandboxValue Value)
{
    public static ProjectionResult NotMatched { get; } = new(false, SandboxValue.Unit);
}

public sealed partial class InstalledKernel
{
    /// <summary>
    /// Runs the event path like <see cref="InvokeAsync{TEvent}"/> — evaluate the lowered <c>ShouldHandle</c>
    /// filter, and only when it matches run the lowered <c>Handle</c> entrypoint — but <b>returns</b> the
    /// <c>Handle</c> result (the lowered <c>Select</c> projection) instead of discarding it. This is the
    /// server-side half of a remote <c>RunLocal</c> chain: the filter and projection always run here, in the
    /// sandbox, and only the projected value crosses the wire.
    /// </summary>
    internal async ValueTask<ProjectionResult> InvokeProjectingAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await AcquireExecutionGateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SandboxRuntimeException) when (IsRevoked)
        {
            // Revoked while queued on the execution gate: skip silently rather than fault the publish.
            return ProjectionResult.NotMatched;
        }

        try
        {
            if (IsRevoked)
            {
                return ProjectionResult.NotMatched;
            }

            var parameters = ValidateFor(adapter);
            var input = BuildInput(adapter, e, _entrypoints.ShouldHandle, parameters);
            var result = await ExecutePreparedAsync(_entrypoints.ShouldHandle, input, cancellationToken).ConfigureAwait(false);
            if (!AsShouldHandleResult(result) || IsRevoked)
            {
                return ProjectionResult.NotMatched;
            }

            if (UsesReusableNoAuditInput(_entrypoints.ShouldHandle) &&
                !UsesReusableNoAuditInput(_entrypoints.Handle))
            {
                input = SnapshotInput(input);
            }

            var projected = await ExecutePreparedAsync(_entrypoints.Handle, input, cancellationToken).ConfigureAwait(false);
            return IsRevoked ? ProjectionResult.NotMatched : new ProjectionResult(true, projected);
        }
        finally
        {
            _executionGate.Release();
        }
    }
}
