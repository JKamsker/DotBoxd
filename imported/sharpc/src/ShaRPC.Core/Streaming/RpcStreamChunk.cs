namespace ShaRPC.Core.Streaming;

internal sealed class RpcStreamChunk : IDisposable
{
    private readonly RpcStreamReceiver _owner;
    private ShaRPC.Core.Buffers.Payload? _frame;

    public RpcStreamChunk(
        RpcStreamReceiver owner,
        ShaRPC.Core.Buffers.Payload frame,
        ReadOnlyMemory<byte> payload)
    {
        _owner = owner;
        _frame = frame;
        Payload = payload;
    }

    public ReadOnlyMemory<byte> Payload { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _frame, null) is { } frame)
        {
            frame.Dispose();
            _owner.ReleaseCredit();
        }
    }

    public void DisposeWithoutCredit() =>
        Interlocked.Exchange(ref _frame, null)?.Dispose();
}
