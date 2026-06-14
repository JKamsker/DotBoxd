namespace DotBoxd.Kernels.Verifier;

/// <summary>
/// A read-only, seekable <see cref="Stream"/> view over an immutable
/// <see cref="ReadOnlyMemory{Byte}"/> buffer. Unlike <see cref="MemoryStream"/>,
/// which requires a <c>byte[]</c> and therefore forces a full defensive copy of
/// the input, this wrapper reads directly from the caller-owned memory. The PE
/// reader consumes it lazily, so verification never allocates a second full-size
/// copy of the assembly image.
/// </summary>
internal sealed class ReadOnlyMemoryStream(ReadOnlyMemory<byte> buffer) : Stream
{
    private readonly ReadOnlyMemory<byte> _buffer = buffer;
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _buffer.Length;

    public override long Position
    {
        get => _position;
        set => _position = checked((int)Seek(value, SeekOrigin.Begin));
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > buffer.Length - offset)
        {
            throw new ArgumentException("The buffer is too small for the requested count.", nameof(count));
        }

        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> destination)
    {
        var remaining = _buffer.Length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        var toCopy = Math.Min(remaining, destination.Length);
        _buffer.Span.Slice(_position, toCopy).CopyTo(destination);
        _position += toCopy;
        return toCopy;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _buffer.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (target < 0 || target > _buffer.Length)
        {
            throw new IOException("An attempt was made to move the position before the beginning or beyond the end of the stream.");
        }

        _position = (int)target;
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
