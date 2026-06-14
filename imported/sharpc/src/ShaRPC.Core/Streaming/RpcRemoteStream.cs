namespace ShaRPC.Core.Streaming;

internal sealed class RpcRemoteStream : Stream
{
    private readonly RpcStreamReceiver _receiver;
    private RpcStreamChunk? _current;
    private int _offset;
    private int _disposed;

    public RpcRemoteStream(RpcStreamReceiver receiver) => _receiver = receiver;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateBuffer(buffer, offset, count);
        return await ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _receiver.Cancel();
            _current?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(RpcRemoteStream));
        }

        if (buffer.Length == 0)
        {
            return 0;
        }

        while (_current is null || _offset >= _current.Payload.Length)
        {
            _current?.Dispose();
            _current = await _receiver.ReadChunkAsync(ct).ConfigureAwait(false);
            _offset = 0;
            if (_current is null)
            {
                return 0;
            }
        }

        var source = _current.Payload.Slice(_offset);
        var count = Math.Min(buffer.Length, source.Length);
        source.Slice(0, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    private static void ValidateBuffer(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if ((uint)offset > (uint)buffer.Length || (uint)count > (uint)(buffer.Length - offset))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
    }
}
