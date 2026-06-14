using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using ShaRPC.Core.Buffers;
using Xunit;

namespace ShaRPC.Tests.Cov.Protocol;

/// <summary>
/// Behavioral coverage for the pooled buffer primitives that back the wire codec:
/// <see cref="Payload"/> (rent/empty/dispose/double-dispose, span/memory boundaries) and
/// <see cref="PooledBufferWriter"/> (capacity defaulting, growth, advance bounds, detach/dispose,
/// zero-byte and large writes).
/// </summary>
public sealed class PayloadCoverageTests
{
    [Fact]
    public void Rent_ZeroLength_ReturnsSharedEmptySingleton()
    {
        // Act
        var first = Payload.Rent(0);
        var second = Payload.Rent(0);

        // Assert: a zero-length rent must hand back the reusable Empty singleton (lines 35-36).
        Assert.Same(Payload.Empty, first);
        Assert.Same(Payload.Empty, second);
        Assert.Equal(0, first.Length);
        Assert.True(first.Span.IsEmpty);
        Assert.True(first.Memory.IsEmpty);
    }

    [Fact]
    public void Empty_Dispose_IsNoOp_AndStaysUsable()
    {
        // Act: disposing Empty (any number of times) must never poison the singleton.
        Payload.Empty.Dispose();
        Payload.Empty.Dispose();

        // Assert
        Assert.Equal(0, Payload.Empty.Length);
        Assert.True(Payload.Empty.Span.IsEmpty);
        Assert.True(Payload.Empty.Memory.IsEmpty);
    }

    [Fact]
    public void Rent_PositiveLength_ProvidesWritableMemoryOfExactLength()
    {
        // Arrange
        const int length = 37;
        var payload = Payload.Rent(length);

        // Act: the receive fill path writes through Memory; Span observes it read-only.
        var memory = payload.Memory;
        for (var i = 0; i < length; i++)
        {
            memory.Span[i] = (byte)(i + 1);
        }

        // Assert
        Assert.Equal(length, payload.Length);
        Assert.Equal(length, payload.Memory.Length);
        Assert.Equal(length, payload.Span.Length);
        Assert.Equal((byte)1, payload.Span[0]);
        Assert.Equal((byte)length, payload.Span[length - 1]);

        payload.Dispose();
    }

    [Fact]
    public void Dispose_OnRealPayload_IsIdempotent()
    {
        // Arrange
        var payload = Payload.Rent(16);

        // Act: double dispose must not double-return the backing array to the pool.
        payload.Dispose();
        payload.Dispose();

        // Assert: after disposal Memory/Span observe the disposed state.
        Assert.Throws<ObjectDisposedException>(() => payload.Memory);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = payload.Span.Length;
        });
    }

    [Fact]
    public async Task Dispose_ConcurrentFromManyThreads_ReturnsBufferExactlyOnce()
    {
        // Arrange: a real payload disposed concurrently — the Interlocked.Exchange guard must ensure
        // only one caller observes the array and returns it. A double-return would corrupt the pool;
        // here we simply assert no exception escapes and the buffer ends disposed.
        var payload = Payload.Rent(64);
        var tasks = new Task[16];

        // Act
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => payload.Dispose());
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Throws<ObjectDisposedException>(() => payload.Memory);
    }
}

/// <summary>
/// Behavioral coverage for <see cref="PooledBufferWriter"/>.
/// </summary>
public sealed class PooledBufferWriterCoverageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Ctor_NonPositiveCapacity_FallsBackToDefaultAndStillWrites(int initialCapacity)
    {
        // Act: a non-positive capacity must be coerced to the default (lines 18-20) and remain usable.
        using var writer = new PooledBufferWriter(initialCapacity);
        var span = writer.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(span, 0x11223344);
        writer.Advance(4);

        // Assert
        Assert.Equal(4, writer.WrittenCount);
        Assert.Equal(0x11223344, BinaryPrimitives.ReadInt32LittleEndian(writer.WrittenMemory.Span));
    }

    [Fact]
    public void Advance_NegativeCount_ThrowsArgumentOutOfRange()
    {
        // Arrange
        using var writer = new PooledBufferWriter();

        // Act + Assert (lines 40-41)
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(-1));
    }

    [Fact]
    public void Advance_PastEndOfBuffer_ThrowsInvalidOperation()
    {
        // Arrange: request a small span, then try to advance further than the buffer can hold.
        using var writer = new PooledBufferWriter(16);
        var capacity = writer.GetSpan(1).Length;

        // Act + Assert (lines 46-47): advancing past capacity is a contract violation.
        var ex = Assert.Throws<InvalidOperationException>(() => writer.Advance(capacity + 1));
        Assert.Contains("Cannot advance past the end of the buffer", ex.Message);
    }

    [Fact]
    public void GetSpan_NegativeSizeHint_ThrowsArgumentOutOfRange()
    {
        // Arrange
        using var writer = new PooledBufferWriter();

        // Act + Assert (lines 96-97)
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.GetSpan(-1));
    }

    [Fact]
    public void GetMemory_NegativeSizeHint_ThrowsArgumentOutOfRange()
    {
        // Arrange
        using var writer = new PooledBufferWriter();

        // Act + Assert: GetMemory shares the same EnsureCapacity guard.
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.GetMemory(-1));
    }

    [Fact]
    public void Advance_ZeroBytes_LeavesWrittenCountUnchanged()
    {
        // Arrange
        using var writer = new PooledBufferWriter();

        // Act: advancing zero is a no-op write.
        _ = writer.GetSpan(8);
        writer.Advance(0);

        // Assert
        Assert.Equal(0, writer.WrittenCount);
        Assert.True(writer.WrittenMemory.IsEmpty);
    }

    [Fact]
    public void GetSpan_WithDefaultHint_StillReturnsAtLeastOneByte()
    {
        // Arrange
        using var writer = new PooledBufferWriter(1);

        // Act: EnsureCapacity guarantees at least one byte even for a zero hint.
        var span = writer.GetSpan();

        // Assert
        Assert.True(span.Length >= 1);
    }

    [Fact]
    public void GetSpan_LargeWrite_ForcesPoolResizeAndPreservesPriorBytes()
    {
        // Arrange: write past the initial capacity so EnsureCapacity rents a bigger buffer and copies.
        using var writer = new PooledBufferWriter(16);
        var prefix = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        prefix.CopyTo(writer.GetSpan(prefix.Length));
        writer.Advance(prefix.Length);

        var large = new byte[64 * 1024];
        new Random(123).NextBytes(large);

        // Act: this write cannot fit the 16-byte initial buffer and forces growth.
        large.CopyTo(writer.GetSpan(large.Length));
        writer.Advance(large.Length);

        // Assert: the resize must preserve the bytes written before growth.
        var written = writer.WrittenMemory.Span;
        Assert.Equal(prefix.Length + large.Length, writer.WrittenCount);
        Assert.Equal(prefix, written.Slice(0, prefix.Length).ToArray());
        Assert.Equal(large, written.Slice(prefix.Length).ToArray());
    }

    [Fact]
    public void GetSpan_RepeatedSmallGrowth_DoublesCapacityAndKeepsData()
    {
        // Arrange / Act: many small advances drive several doubling resizes.
        using var writer = new PooledBufferWriter(4);
        for (var i = 0; i < 1000; i++)
        {
            var span = writer.GetSpan(1);
            span[0] = (byte)(i & 0xFF);
            writer.Advance(1);
        }

        // Assert
        Assert.Equal(1000, writer.WrittenCount);
        var written = writer.WrittenMemory.Span;
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal((byte)(i & 0xFF), written[i]);
        }
    }

    [Fact]
    public void DetachPayload_TransfersOwnership_AndDisposeBecomesNoOp()
    {
        // Arrange
        var writer = new PooledBufferWriter(8);
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        bytes.CopyTo(writer.GetSpan(bytes.Length));
        writer.Advance(bytes.Length);

        // Act
        var payload = writer.DetachPayload();

        // Assert: the payload owns the bytes; the now-detached writer's Dispose is a safe no-op.
        Assert.Equal(bytes.Length, payload.Length);
        Assert.Equal(bytes, payload.Span.ToArray());
        writer.Dispose(); // must not return the array a second time
        payload.Dispose();
    }

    [Fact]
    public void DetachPayload_CalledTwice_ThrowsInvalidOperation()
    {
        // Arrange
        var writer = new PooledBufferWriter();
        var first = writer.DetachPayload();

        // Act + Assert: ownership was already relinquished.
        var ex = Assert.Throws<InvalidOperationException>(() => writer.DetachPayload());
        Assert.Contains("already been detached or disposed", ex.Message);

        first.Dispose();
    }

    [Fact]
    public void WrittenMemory_AfterDispose_ThrowsObjectDisposed()
    {
        // Arrange
        var writer = new PooledBufferWriter();
        writer.Dispose();

        // Act + Assert
        Assert.Throws<ObjectDisposedException>(() => writer.WrittenMemory);
    }

    [Fact]
    public void Dispose_CalledTwice_IsNoOp()
    {
        // Arrange
        var writer = new PooledBufferWriter();

        // Act + Assert: a second dispose must not double-return the buffer.
        writer.Dispose();
        writer.Dispose();
    }

    [Fact]
    public void DetachPayload_OnEmptyWriter_ProducesZeroLengthPayload()
    {
        // Arrange: nothing written.
        var writer = new PooledBufferWriter();

        // Act
        var payload = writer.DetachPayload();

        // Assert: a zero-length detach yields an empty-but-valid payload.
        Assert.Equal(0, payload.Length);
        Assert.True(payload.Span.IsEmpty);
        payload.Dispose();
    }
}
