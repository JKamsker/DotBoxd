namespace SafeIR;

/// <summary>
/// A wall-time <see cref="CancellationTokenSource"/> that is reused for the
/// lifetime of a single sandbox run when the run cancellation token is not
/// cancelable. On that fast path there is no asynchronous run cancellation to
/// link, so binding dispatch does not need a fresh linked source per call: the
/// deadline is already tracked by the <see cref="ResourceMeter"/> and the timer
/// is only ever (re)armed here.
/// </summary>
/// <remarks>
/// Dispatchers dispose the source they receive in their <c>finally</c> block.
/// This type makes <see cref="Dispose(bool)"/> a no-op so the shared instance
/// survives those calls and stays reusable. The owning <see cref="SandboxContext"/>
/// arms the deadline timer via <see cref="ArmDeadline"/> before each dispatch,
/// which reuses the internal timer (no per-call allocation) instead of creating
/// a new source. Once the deadline fires, the enforcing <c>Timeout</c> error
/// aborts the run, so the cancelled instance is never reused afterwards.
/// </remarks>
internal sealed class SharedWallTimeTokenSource : CancellationTokenSource
{
    public void ArmDeadline(TimeSpan remaining)
    {
        // CancelAfter reuses the source's internal timer after the first call,
        // so re-arming the shared instance does not allocate per binding call.
        // A source that has already fired (run is aborting) ignores this.
        if (!IsCancellationRequested)
        {
            CancelAfter(remaining);
        }
    }

    // Dispatchers dispose the returned source unconditionally; keep the shared
    // instance alive so it can be handed out and re-armed on the next call.
    protected override void Dispose(bool disposing)
    {
    }
}
