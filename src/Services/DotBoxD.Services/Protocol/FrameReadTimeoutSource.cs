namespace DotBoxD.Services.Protocol;

internal sealed class FrameReadTimeoutSource : IDisposable
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private CancellationTokenSource? _source;
    private CancellationToken _ownerToken;
    private bool _ownerTokenCanCancel;

    public static TimeSpan Resolve(TimeSpan? timeout, string parameterName) =>
        Validate(timeout ?? DefaultIdleTimeout, parameterName);

    public static TimeSpan Resolve(TimeSpan? timeout, TimeSpan defaultTimeout, string parameterName) =>
        Validate(timeout ?? defaultTimeout, parameterName);

    public static TimeSpan Validate(TimeSpan timeout, string parameterName)
    {
        if (timeout == Timeout.InfiniteTimeSpan ||
            (timeout > TimeSpan.Zero && timeout.TotalMilliseconds <= int.MaxValue))
        {
            return timeout;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            timeout,
            "Frame read idle timeout must be positive (at most int.MaxValue ms) or Timeout.InfiniteTimeSpan.");
    }

    public async ValueTask<int> ReadAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken ownerToken,
        TimeSpan timeout)
    {
        var readToken = Start(ownerToken, timeout);
        try
        {
            return await stream.ReadAsync(buffer, readToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsTimeoutCancellation(ownerToken))
        {
            throw new IOException(
                $"Inbound frame read stalled for longer than {timeout} with no data (possible slow-loris peer).");
        }
        finally
        {
            CancelPendingTimeout();
        }
    }

    public CancellationToken Start(CancellationToken ownerToken, TimeSpan timeout)
    {
        var source = _source;
        if (source is null ||
            source.IsCancellationRequested ||
            !MatchesOwner(ownerToken))
        {
            source?.Dispose();
            source = ownerToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ownerToken)
                : new CancellationTokenSource();
            _source = source;
            _ownerToken = ownerToken;
            _ownerTokenCanCancel = ownerToken.CanBeCanceled;
        }

        source.CancelAfter(timeout);
        return source.Token;
    }

    public bool IsTimeoutCancellation(CancellationToken ownerToken)
    {
        var source = _source;
        return source is not null &&
            source.IsCancellationRequested &&
            !ownerToken.IsCancellationRequested;
    }

    public void CancelPendingTimeout()
    {
        var source = _source;
        if (source is { IsCancellationRequested: false })
        {
            try
            {
                source.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Dispose can race a read finally block that is clearing a pending timeout.
            }
        }
    }

    public void Dispose()
    {
        _source?.Dispose();
        _source = null;
    }

    private bool MatchesOwner(CancellationToken ownerToken) =>
        _ownerTokenCanCancel == ownerToken.CanBeCanceled &&
        (!_ownerTokenCanCancel || _ownerToken.Equals(ownerToken));
}
