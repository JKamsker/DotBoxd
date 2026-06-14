using ShaRPC.Core.Buffers;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 6 regression for <see cref="PooledBufferWriter.Advance"/>. Its bounds check
/// <c>_written + count &gt; buffer.Length</c> used 32-bit signed arithmetic, so a large <c>count</c>
/// overflows to a negative value, the guard passes, and <c>_written</c> is advanced past the buffer into a
/// corrupt (negative) state with no exception. The R5#4 fix widened the symmetric check in
/// <c>EnsureCapacity</c> but not this one.
/// </summary>
public sealed class Round6_PooledBufferWriterAdvanceOverflowTests
{
    [Fact]
    public void Advance_WhenWrittenPlusCountOverflowsInt_ThrowsInsteadOfCorruptingState()
    {
        using var writer = new PooledBufferWriter(256);
        writer.GetSpan(1);
        writer.Advance(1); // _written = 1

        // 1 + int.MaxValue overflows 32-bit signed to a negative value; the guard then wrongly passes and
        // _written is corrupted. The fix must reject the over-advance (widen the check to 64-bit).
        Assert.Throws<InvalidOperationException>(() => writer.Advance(int.MaxValue));
    }
}
