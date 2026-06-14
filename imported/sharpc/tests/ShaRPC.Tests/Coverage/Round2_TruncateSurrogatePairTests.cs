using System.Text;
using ShaRPC.Core;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 2 regression coverage for <see cref="RpcErrors.Truncate"/> (defect #14). Truncation cuts the
/// reflected value at the raw UTF-16 char index <c>MaxReflectedValueLength - 3</c> via
/// <see cref="string.Substring(int, int)"/> with no surrogate-boundary check. When that index lands
/// between the high and low halves of a surrogate pair (e.g. an astral emoji), the result ends in an
/// orphaned high surrogate — a malformed UTF-16 string. The cut must instead respect surrogate
/// boundaries so the truncated value stays well-formed.
/// </summary>
public sealed class Round2_TruncateSurrogatePairTests
{
    [Fact]
    public void Truncate_DoesNotSplitSurrogatePair_AtCutBoundary()
    {
        // Arrange: position an astral emoji so its high surrogate sits exactly at the cut index.
        // Substring(0, cap - 3) keeps chars [0, cap-4]; placing the high surrogate at index (cap-4)
        // and the low surrogate at (cap-3) means the naive cut retains the high half but drops the low
        // half, leaving an unpaired high surrogate as the last payload char before the "..." ellipsis.
        const int cap = RpcErrors.MaxReflectedValueLength;
        const int cutIndex = cap - 3;
        const int highSurrogateIndex = cutIndex - 1;

        // U+1F600 GRINNING FACE is a surrogate pair: high D83D, low DE00.
        const string emoji = "😀";

        var input = new string('A', highSurrogateIndex) + emoji + new string('B', cap);
        Assert.True(input.Length > cap, "input must exceed the cap so truncation runs");
        Assert.True(char.IsHighSurrogate(input[highSurrogateIndex]), "high surrogate must sit at the cut boundary");
        Assert.True(char.IsLowSurrogate(input[cutIndex]), "low surrogate must sit just past the cut boundary");

        // Act
        var result = RpcErrors.Truncate(input);

        // Assert: the truncated value must stay within the cap and contain no unpaired surrogate.
        Assert.True(result.Length <= cap, $"truncated length {result.Length} must not exceed cap {cap}");
        Assert.EndsWith("...", result);
        AssertNoUnpairedSurrogate(result);
    }

    private static void AssertNoUnpairedSurrogate(string value)
    {
        // A well-formed UTF-16 string round-trips through strict UTF-8 (which rejects unpaired
        // surrogates) without throwing. An orphaned high surrogate makes GetBytes throw.
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        var ex = Record.Exception(() => strict.GetBytes(value));
        Assert.True(
            ex is null,
            "truncated value contains an unpaired surrogate (malformed UTF-16): " + ex?.Message);

        // Defensive cross-check: no char is a lone surrogate.
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                Assert.True(
                    i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]),
                    $"unpaired high surrogate at index {i}");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(value[i]), $"unpaired low surrogate at index {i}");
            }
        }
    }
}
