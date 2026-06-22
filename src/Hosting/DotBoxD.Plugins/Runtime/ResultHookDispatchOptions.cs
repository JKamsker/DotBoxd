namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Per-dispatch options for result-returning hooks. Local handlers keep the existing in-process semantics; the
/// timeout applies only to remote result handlers because they cross an IPC boundary that can stall.
/// </summary>
public sealed class ResultHookDispatchOptions<TResult>
    where TResult : struct, IHookResult
{
    private static readonly TimeSpan MaxRemoteHandlerTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    public static ResultHookDispatchOptions<TResult> Default { get; } = new();

    public TimeSpan RemoteHandlerTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    public TResult? RemoteTimeoutResult { get; init; }

    public static ResultHookDispatchOptions<TResult> FailClosedAfter(TimeSpan timeout, TResult result)
        => new()
        {
            RemoteHandlerTimeout = timeout,
            RemoteTimeoutResult = result,
        };

    internal void Validate()
    {
        if (RemoteHandlerTimeout == Timeout.InfiniteTimeSpan ||
            (RemoteHandlerTimeout > TimeSpan.Zero && RemoteHandlerTimeout <= MaxRemoteHandlerTimeout))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(RemoteHandlerTimeout),
            RemoteHandlerTimeout,
            $"Remote handler timeout must be positive, no greater than {MaxRemoteHandlerTimeout}, or Timeout.InfiniteTimeSpan.");
    }
}
