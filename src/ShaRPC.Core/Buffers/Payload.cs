using System.Buffers;

namespace ShaRPC.Core.Buffers;

/// <summary>
/// A single-owner buffer rented from <see cref="ArrayPool{T}"/>. Dispose returns the
/// backing array to the pool exactly once. Treat <see cref="Memory"/> as read-only after
/// the buffer has been handed off (it is mutable only to support the receive fill path and
/// the <see cref="IMemoryOwner{T}"/> contract).
/// </summary>
public sealed class Payload : IMemoryOwner<byte>
{
    /// <summary>
    /// Shared empty payload. Wraps <see cref="Array.Empty{T}"/>; <see cref="Dispose"/> is a
    /// guaranteed no-op so the singleton stays reusable.
    /// </summary>
    public static Payload Empty { get; } = new(Array.Empty<byte>(), 0);

    private byte[]? _array;
    private readonly int _length;

    internal Payload(byte[] rented, int length)
    {
        _array = rented;
        _length = length;
    }

    /// <summary>
    /// Rents a payload of at least <paramref name="length"/> bytes. A zero length returns the
    /// shared <see cref="Empty"/> singleton.
    /// </summary>
    public static Payload Rent(int length)
    {
        if (length == 0)
        {
            return Empty;
        }

        return new Payload(ArrayPool<byte>.Shared.Rent(length), length);
    }

    /// <summary>
    /// The usable region of the rented buffer. Mutable to support the receive fill path; treat
    /// as read-only once the payload has been received.
    /// </summary>
    public Memory<byte> Memory =>
        (_array ?? throw new ObjectDisposedException(nameof(Payload))).AsMemory(0, _length);

    /// <summary>
    /// A read-only view of the usable region of the rented buffer.
    /// </summary>
    public ReadOnlySpan<byte> Span =>
        (_array ?? throw new ObjectDisposedException(nameof(Payload))).AsSpan(0, _length);

    /// <summary>
    /// The number of usable bytes in the payload.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Returns the backing array to the pool. Idempotent and safe to call on <see cref="Empty"/>.
    /// </summary>
    public void Dispose()
    {
        // Empty wraps a zero-length array (Rent never returns one) and has _length 0, so skipping
        // it here keeps the Empty singleton reusable — its _array must never be nulled. For real
        // payloads the Interlocked.Exchange makes Dispose both idempotent and thread-safe: only the
        // thread that observes the non-null array returns it, so concurrent or repeated Dispose can
        // never double-return the same rented buffer to the pool.
        if (_length == 0)
        {
            return;
        }

        var array = Interlocked.Exchange(ref _array, null);
        if (array is not null)
        {
            ArrayPool<byte>.Shared.Return(array);
        }
    }
}
