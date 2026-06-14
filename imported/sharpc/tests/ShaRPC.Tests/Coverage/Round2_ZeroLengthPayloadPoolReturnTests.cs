using System;
using System.Buffers;
using ShaRPC.Core.Buffers;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// RED regression for DEFECT #7: <see cref="Payload.Dispose"/> skips the pool return for a
/// zero-length payload that owns a REAL rented buffer.
///
/// <para>
/// <see cref="PooledBufferWriter.DetachPayload"/> on a writer with nothing written hands a real
/// 256-byte array rented from <see cref="ArrayPool{T}"/> to <c>new Payload(buffer, 0)</c>. The
/// <c>_length == 0</c> early-return guard in <see cref="Payload.Dispose"/> was intended only to keep
/// the <see cref="Payload.Empty"/> singleton (whose array is <see cref="Array.Empty{T}"/>) reusable,
/// but it also fires for this zero-written detach, so the rented buffer is never returned and the
/// backing array is never nulled.
/// </para>
///
/// <para>
/// The observable, parallel-safe signal is the post-dispose state of the backing array: for a real
/// rented buffer, a correct <c>Dispose</c> nulls <c>_array</c> (so <see cref="Payload.Memory"/> and
/// <see cref="Payload.Span"/> throw <see cref="ObjectDisposedException"/>) AND returns the array to the
/// pool. The buggy early-return leaves <c>_array</c> intact, so <see cref="Payload.Memory"/> keeps
/// working — that is what fails here today. The suggested fix replaces the length guard with a
/// reference check against <see cref="Array.Empty{T}"/>, which leaves the singleton protected while
/// disposing real zero-length buffers normally.
/// </para>
/// </summary>
public sealed class Round2_ZeroLengthPayloadPoolReturnTests
{
    [Fact]
    public void Dispose_ZeroLengthDetachedPayload_ReleasesRealRentedBuffer()
    {
        // Arrange: an empty writer rents a real 256-byte backing array, then detaches it with
        // nothing written -> a Payload over a pool-owned array whose Length is 0.
        var writer = new PooledBufferWriter();
        var payload = writer.DetachPayload();
        Assert.Equal(0, payload.Length);

        // Act: disposing a payload that owns a real rented buffer must release ownership of that
        // buffer (null the backing array and return it to the pool), exactly as it does for a
        // non-empty payload. The Empty-singleton protection must not extend to real rented arrays.
        payload.Dispose();

        // Assert: after a real release the backing array is gone, so the usable-region accessors
        // observe the disposed state. RED today: the _length == 0 guard skips the release, leaves the
        // 256-byte rented buffer attached (and leaked to the pool), and Memory/Span keep returning an
        // empty view instead of throwing.
        Assert.Throws<ObjectDisposedException>(() => payload.Memory);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = payload.Span.Length;
        });
    }

    [Fact]
    public void Dispose_ZeroLengthPayloadOverPoolRentedArray_ReleasesBuffer()
    {
        // Arrange: directly model the leak independent of PooledBufferWriter -- a Payload that owns a
        // pool-rented array but reports Length 0. This mirrors the DetachPayload(_written == 0) shape.
        var rented = ArrayPool<byte>.Shared.Rent(256);
        var payload = new Payload(rented, 0);

        // Act
        payload.Dispose();

        // Assert: ownership of the rented buffer must be relinquished -> disposed state observed.
        // RED today because the _length == 0 guard returns before nulling/returning the array.
        Assert.Throws<ObjectDisposedException>(() => payload.Memory);
    }

    [Fact]
    public void Dispose_EmptySingleton_StaysReusable_AfterFix()
    {
        // Guard rail (passes before and after the fix): the corrected guard must still protect the
        // shared Empty singleton, whose array is Array.Empty<byte>() rather than a pool-rented buffer.
        // This documents the intended boundary so the fix cannot regress the singleton.
        Payload.Empty.Dispose();
        Payload.Empty.Dispose();

        Assert.Equal(0, Payload.Empty.Length);
        Assert.True(Payload.Empty.Span.IsEmpty);
        Assert.True(Payload.Empty.Memory.IsEmpty);
    }
}
