using ShaRPC.Core.Buffers;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 5 regression for <see cref="PooledBufferWriter"/>. <c>EnsureCapacity</c> computed
/// <c>required = _written + Math.Max(sizeHint, 1)</c> in unchecked 32-bit signed arithmetic. A large
/// <c>sizeHint</c> overflows to a negative value, so the guard <c>required &lt;= buffer.Length</c> passes
/// and <c>GetSpan</c> silently returns a span far smaller than the requested size — an
/// <see cref="System.Buffers.IBufferWriter{T}"/> contract violation that a correct caller (which checks
/// <c>span.Length</c>) cannot detect. The writer must refuse the request instead of truncating.
/// </summary>
public sealed class Round5_PooledBufferWriterOverflowTests
{
    [Fact]
    public void GetSpan_WhenWrittenPlusSizeHintOverflowsInt_DoesNotSilentlyReturnTruncatedSpan()
    {
        using var writer = new PooledBufferWriter(256);
        writer.GetSpan(1);
        writer.Advance(1); // _written = 1

        // 1 + int.MaxValue overflows 32-bit signed to a negative value on the buggy path, the guard passes,
        // and a ~255-byte span is returned with no error. The fix refuses an unsatisfiable request.
        Assert.Throws<OutOfMemoryException>(() => { _ = writer.GetSpan(int.MaxValue); });
    }

    [Fact]
    public void GetSpan_WithModerateSizeHint_StillGrowsAndHonorsTheContract()
    {
        // Guard the fix: ordinary (non-overflowing) growth must still satisfy the requested sizeHint.
        using var writer = new PooledBufferWriter(16);
        writer.GetSpan(4);
        writer.Advance(4);

        var span = writer.GetSpan(1024);

        Assert.True(span.Length >= 1024, $"span length {span.Length} must be at least the requested 1024");
    }
}
