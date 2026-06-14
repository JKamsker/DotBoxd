using System.Text;
using ShaRPC.Core;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 4 regression for <see cref="RpcErrors.Truncate"/>. The Round 2 surrogate-safe fix backed off the
/// cut only when the last kept char was a HIGH surrogate. A lone LOW surrogate at the boundary — present in
/// already-malformed UTF-16 input, e.g. a reflected value a caller built badly — was still kept, leaving the
/// truncated value ending in an unpaired surrogate that breaks strict UTF-8 encoding (the exact failure
/// Round 2 set out to prevent). The boundary check must also drop a lone low surrogate, while NOT splitting
/// a valid pair whose low half completes a high surrogate inside the kept region.
/// </summary>
public sealed class Round4_TruncateLoneLowSurrogateTests
{
    // An unpaired UTF-16 low surrogate. Built from its code point so this file holds no raw lone surrogate
    // (which is not even valid UTF-8 and would corrupt the source on disk).
    private const char LoneLowSurrogate = (char)0xDE00;

    [Fact]
    public void Truncate_DropsLoneLowSurrogate_AtCutBoundary()
    {
        const int cap = RpcErrors.MaxReflectedValueLength;
        const int cutIndex = cap - 3;            // first dropped char
        const int boundaryIndex = cutIndex - 1;  // last kept char

        // A lone low surrogate at the last-kept index, preceded by a plain BMP char (no high surrogate),
        // so it is genuinely unpaired and not the tail of a valid pair.
        var input = new string('A', boundaryIndex) + LoneLowSurrogate + new string('B', cap);

        Assert.True(input.Length > cap, "input must exceed the cap so truncation runs");
        Assert.False(
            char.IsHighSurrogate(input[boundaryIndex - 1]),
            "the preceding char must not pair with the low surrogate, so it stays unpaired");
        Assert.True(char.IsLowSurrogate(input[boundaryIndex]), "lone low surrogate must sit at the cut boundary");

        var result = RpcErrors.Truncate(input);

        Assert.True(result.Length <= cap, $"truncated length {result.Length} must not exceed cap {cap}");
        AssertWellFormedUtf16(result);
    }

    [Fact]
    public void Truncate_KeepsValidPair_WhenLowSurrogateAtBoundaryCompletesAHighSurrogate()
    {
        // Regression guard for the fix itself: when the boundary low surrogate is the second half of a
        // VALID pair (high surrogate just before it), the pair must be kept intact, not split — a naive
        // "back off on any surrogate" fix would orphan the retained high half.
        const int cap = RpcErrors.MaxReflectedValueLength;
        const int cutIndex = cap - 3;
        const int boundaryIndex = cutIndex - 1;  // low half kept here
        const int highIndex = boundaryIndex - 1; // high half kept here

        var emoji = char.ConvertFromUtf32(0x1F600); // GRINNING FACE: high D83D, low DE00
        var input = new string('A', highIndex) + emoji + new string('B', cap);

        Assert.True(char.IsHighSurrogate(input[highIndex]), "high half must sit just before the boundary");
        Assert.True(char.IsLowSurrogate(input[boundaryIndex]), "low half must sit at the boundary");

        var result = RpcErrors.Truncate(input);

        AssertWellFormedUtf16(result);
        Assert.EndsWith(emoji + "...", result); // the full pair survives truncation
    }

    private static void AssertWellFormedUtf16(string value)
    {
        // A well-formed UTF-16 string round-trips through strict UTF-8, which rejects unpaired surrogates.
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var ex = Record.Exception(() => strict.GetBytes(value));
        Assert.True(ex is null, "truncated value contains an unpaired surrogate (malformed UTF-16): " + ex?.Message);
    }
}
