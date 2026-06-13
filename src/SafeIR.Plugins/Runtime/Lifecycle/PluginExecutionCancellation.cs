namespace SafeIR.Plugins;

/// <summary>
/// Resolves the effective cancellation token for a single kernel entrypoint
/// execution while avoiding a linked <see cref="CancellationTokenSource"/> on the
/// common path. A linked source is only allocated when both the caller token and
/// the kernel revocation token can independently fire; otherwise the single
/// cancelable token (or the non-cancelable caller token) is passed through directly.
/// Dispose the returned scope to release the linked source when one was created.
/// </summary>
internal readonly struct PluginExecutionCancellation : IDisposable
{
    private readonly CancellationTokenSource? _linked;

    private PluginExecutionCancellation(CancellationToken token, CancellationTokenSource? linked)
    {
        Token = token;
        _linked = linked;
    }

    public CancellationToken Token { get; }

    public static PluginExecutionCancellation Create(CancellationToken caller, CancellationToken revocation)
    {
        var callerCancelable = caller.CanBeCanceled;
        var revocationCancelable = revocation.CanBeCanceled;

        if (!callerCancelable)
        {
            // No caller cancellation to honor; revocation alone (cancelable or not)
            // is already observable through its own token, so link nothing.
            return new PluginExecutionCancellation(revocation, null);
        }

        if (!revocationCancelable)
        {
            // Revocation can never fire; the caller token alone carries cancellation.
            return new PluginExecutionCancellation(caller, null);
        }

        // Both tokens can independently fire: a linked source is required so either
        // one cancels the execution. The caller disposes it via the scope.
        var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, revocation);
        return new PluginExecutionCancellation(linked.Token, linked);
    }

    public void Dispose() => _linked?.Dispose();
}
