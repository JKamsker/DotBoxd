namespace DotBoxD.Services.Tests.Coverage.Transport;

/// <summary>Stream that returns at most <c>bytesPerRead</c> bytes per <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> call.</summary>
internal sealed class DripStream : Stream
{
    private readonly byte[] _data;
    private readonly int _bytesPerRead;
    private int _position;

    public DripStream(byte[] data, int bytesPerRead)
    {
        _data = data;
        _bytesPerRead = Math.Max(1, bytesPerRead);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _data.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_position >= _data.Length)
        {
            return ValueTask.FromResult(0);
        }

        var count = Math.Min(Math.Min(_bytesPerRead, buffer.Length), _data.Length - _position);
        _data.AsSpan(_position, count).CopyTo(buffer.Span);
        _position += count;
        return ValueTask.FromResult(count);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Yields the supplied 4-byte length prefix, then faults on the next body read.
/// </summary>
internal sealed class FaultAfterPrefixStream : Stream
{
    private readonly byte[] _prefix;
    private int _position;

    public FaultAfterPrefixStream(byte[] prefix) => _prefix = prefix;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _prefix.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_position < _prefix.Length)
        {
            var count = Math.Min(buffer.Length, _prefix.Length - _position);
            _prefix.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            return ValueTask.FromResult(count);
        }

        throw new IOException("boom");
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Read blocks forever until the cancellation token fires.</summary>
internal sealed class NeverReturnsStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => 0;
    public override long Position { get => 0; set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return 0;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Counts how many times the stream was disposed, to assert ownership semantics.</summary>
internal sealed class TrackingDisposeStream : Stream
{
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get => 0; set => throw new NotSupportedException(); }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        ValueTask.FromResult(0);

    public override int Read(byte[] buffer, int offset, int count) => 0;

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Increment(ref _disposeCount);
        }

        base.Dispose(disposing);
    }
}
