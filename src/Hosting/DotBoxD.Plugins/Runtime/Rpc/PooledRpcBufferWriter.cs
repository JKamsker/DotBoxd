namespace DotBoxD.Plugins.Runtime.Rpc;

using System.Buffers;

/// <summary>
/// A minimal <see cref="IBufferWriter{T}"/> backed by an array rented from <see cref="ArrayPool{T}"/>, used to
/// encode a single <c>RunLocal</c> / server-extension payload without the per-event <c>MemoryStream</c> + growth
/// + <c>ToArray</c> the codec's <c>byte[]</c> overloads incur.
/// </summary>
/// <remarks>
/// <para>
/// This is the core of CommunityToolkit's <c>ArrayPoolBufferWriter&lt;T&gt;</c> (rent the buffer, grow by
/// doubling, return it on <see cref="Dispose"/>), trimmed to this path's needs. It deliberately does NOT pool
/// the writer object itself — one instance is created per encode and discarded — so there is no shared or
/// concurrent state, and therefore nothing concurrency-subtle to get wrong. The per-encode object is a small
/// gen0 allocation; the buffer it manages is fully pooled, which is where the cost lives.
/// </para>
/// <para>
/// Lifetime contract: encode into the writer, hand <see cref="WrittenMemory"/> to a transport, and only
/// <see cref="Dispose"/> it <i>after</i> that transport has finished copying the bytes — the rented array
/// aliases <see cref="WrittenMemory"/>, so returning it to the pool while a send still reads from it would be a
/// use-after-free. The remote push path satisfies this by disposing in a <c>using</c> whose scope ends after
/// <c>await push(...)</c> completes. Cannot reuse <c>DotBoxD.Services.Buffers.PooledBufferWriter</c>:
/// <c>DotBoxD.Plugins</c> does not (and should not) reference the RPC transport layer.
/// </para>
/// </remarks>
internal sealed class PooledRpcBufferWriter : IBufferWriter<byte>, IDisposable
{
    // Largest single-dimension array the runtime allows (== Array.MaxLength).
    private const int MaxArrayLength = 0x7FFFFFC7;
    private const int DefaultCapacity = 256;

    private byte[]? _buffer;
    private int _written;

    public PooledRpcBufferWriter(int initialCapacity = DefaultCapacity)
        => _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 1));

    /// <summary>Creates a writer that rents its buffer from <see cref="ArrayPool{T}.Shared"/>.</summary>
    public static PooledRpcBufferWriter Rent(int initialCapacity = DefaultCapacity) => new(initialCapacity);

    /// <summary>The bytes written so far. Valid only until <see cref="Dispose"/> returns the array to the pool.</summary>
    public ReadOnlyMemory<byte> WrittenMemory =>
        (_buffer ?? throw new ObjectDisposedException(nameof(PooledRpcBufferWriter))).AsMemory(0, _written);

    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledRpcBufferWriter));
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

    /// <summary>Returns the rented array to the pool. Idempotent — safe to call once the encode is done.</summary>
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
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledRpcBufferWriter));
        var required = (long)_written + Math.Max(sizeHint, 1);
        if (required <= buffer.Length)
        {
            return;
        }

        if (required > MaxArrayLength)
        {
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
