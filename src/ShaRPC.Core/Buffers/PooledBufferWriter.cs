using System.Buffers;

namespace ShaRPC.Core.Buffers;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> backed by an array rented from <see cref="ArrayPool{T}"/>.
/// Either hand the written bytes off via <see cref="DetachPayload"/> or release them via
/// <see cref="Dispose"/> — never both.
/// </summary>
public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    // Largest single-dimension array the runtime allows (== Array.MaxLength, which netstandard2.1 lacks).
    private const int MaxArrayLength = 0x7FFFFFC7;

    private byte[]? _buffer;
    private int _written;

    public PooledBufferWriter(int initialCapacity = 256)
    {
        if (initialCapacity <= 0)
        {
            initialCapacity = 256;
        }

        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _written = 0;
    }

    /// <summary>
    /// The bytes written so far.
    /// </summary>
    public ReadOnlyMemory<byte> WrittenMemory =>
        (_buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter))).AsMemory(0, _written);

    /// <summary>
    /// The number of bytes written so far.
    /// </summary>
    public int WrittenCount => _written;

    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
        // Widen to 64-bit: _written + count in 32-bit signed arithmetic can overflow to a negative value,
        // making this guard pass and silently corrupting _written (symmetric with the EnsureCapacity fix).
        if ((long)_written + count > buffer.Length)
        {
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");
        }

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer!.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer!.AsSpan(_written);
    }

    /// <summary>
    /// Hands the rented array and written length to a new <see cref="Payload"/> and relinquishes
    /// ownership. The writer must not be used afterward.
    /// </summary>
    public Payload DetachPayload()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null)
            ?? throw new InvalidOperationException("Buffer has already been detached or disposed.");
        return new Payload(buffer, _written);
    }

    /// <summary>
    /// Returns the rented array to the pool. A no-op after <see cref="DetachPayload"/>.
    /// </summary>
    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void EnsureCapacity(int sizeHint)
    {
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));

        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        // Widen to 64-bit: _written + sizeHint in 32-bit signed arithmetic can overflow to a negative
        // value, making the guard below pass and handing back a span SMALLER than sizeHint (an
        // IBufferWriter<T> contract break the caller cannot detect).
        var required = (long)_written + Math.Max(sizeHint, 1);
        if (required <= buffer.Length)
        {
            return;
        }

        if (required > MaxArrayLength)
        {
            // The request cannot be satisfied by a single array; refuse rather than silently truncate.
            throw new OutOfMemoryException(
                $"Requested buffer capacity ({required}) exceeds the maximum array length ({MaxArrayLength}).");
        }

        var newSize = (int)Math.Min(Math.Max(required, (long)buffer.Length * 2), MaxArrayLength);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        Array.Copy(buffer, 0, newBuffer, 0, _written);
        _buffer = newBuffer;
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
