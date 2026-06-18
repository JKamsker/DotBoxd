namespace DotBoxD.Services.Transport;

internal sealed class FrameReadTimeoutSource : IDisposable
{
    private CancellationTokenSource? _source;
    private CancellationToken _ownerToken;
    private bool _ownerTokenCanCancel;

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
            source.CancelAfter(Timeout.InfiniteTimeSpan);
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
