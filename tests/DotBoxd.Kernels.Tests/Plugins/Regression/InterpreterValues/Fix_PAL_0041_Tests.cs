using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0041: the untyped
/// <see cref="SandboxValue.FromList(IReadOnlyList{SandboxValue})"/> factory snapshots
/// the input twice. It first calls <c>ModelCopy.List(values)</c> to infer the item type,
/// then hands that fresh snapshot to the <see cref="ListValue"/> constructor, which
/// snapshots it again because it is not an owned snapshot. That is two N-element arrays
/// plus two read-only wrappers for one immutable list value.
///
/// The typed overload <see cref="SandboxValue.FromList(IReadOnlyList{SandboxValue}, SandboxType)"/>
/// performs exactly one snapshot (only the <see cref="ListValue"/> constructor copies).
/// Both factories must produce structurally identical, defensively-copied values, so the
/// untyped factory should cost essentially the same as the typed factory. While the bug
/// is present the untyped factory allocates roughly one extra N-element array, so its
/// per-call allocation is far above the typed factory's.
/// </summary>
public sealed class Fix_PAL_0041_Tests
{
    // Large enough that one extra reference array (8 bytes/element on 64-bit)
    // dominates fixed per-call bookkeeping and JIT noise.
    private const int ElementCount = 10_000;

    private static SandboxValue[] BuildSource()
    {
        var source = new SandboxValue[ElementCount];
        for (var i = 0; i < ElementCount; i++)
        {
            source[i] = SandboxValue.FromInt32(i);
        }

        return source;
    }

    private static long PerCallAllocation(Func<SandboxValue> factory)
    {
        // Warm up: JIT the factory path and any one-time allocations so the
        // measured loop reflects only steady-state per-call cost.
        _ = factory();

        const int iterations = 16;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            _ = factory();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    [Fact]
    public void Untyped_FromList_does_not_allocate_a_second_snapshot()
    {
        var source = BuildSource();

        var typedPerCall = PerCallAllocation(
            () => SandboxValue.FromList(source, SandboxType.I32));
        var untypedPerCall = PerCallAllocation(
            () => SandboxValue.FromList(source));

        // One reference snapshot of the source is roughly ElementCount * 8 bytes
        // (64-bit object references) plus a small ReadOnlyCollection wrapper.
        const long referenceArrayBytes = ElementCount * 8L;

        // RED until PAL-0041 is fixed: the untyped factory snapshots twice, so it
        // allocates about one extra reference array compared to the single-snapshot
        // typed factory. The fixed item-type inference is O(1), so once the double
        // snapshot is removed the untyped factory should stay within a small fraction
        // of an array of the typed factory's cost.
        Assert.True(
            untypedPerCall - typedPerCall < referenceArrayBytes / 2,
            $"Untyped FromList allocated ~{untypedPerCall} bytes/call vs ~{typedPerCall} " +
            $"bytes/call for the typed factory over {ElementCount} elements. The extra " +
            $"~{untypedPerCall - typedPerCall} bytes indicate a second defensive snapshot " +
            "of the same element sequence.");
    }

    [Fact]
    public void Untyped_and_typed_FromList_produce_equal_values()
    {
        var source = BuildSource();

        var untyped = SandboxValue.FromList(source);
        var typed = SandboxValue.FromList(source, SandboxType.I32);

        // The fix must preserve observable behavior: same structure, type, and hash.
        Assert.Equal(typed, untyped);
        Assert.Equal(typed.GetHashCode(), untyped.GetHashCode());
        Assert.Equal(SandboxType.List(SandboxType.I32), untyped.Type);
    }

    [Fact]
    public void Untyped_FromList_keeps_a_defensive_copy()
    {
        var source = new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) };

        var list = (ListValue)SandboxValue.FromList(source);

        // Mutating the caller's array after construction must not affect the value:
        // the single retained snapshot must still be an isolated copy.
        source[0] = SandboxValue.FromInt32(99);

        Assert.Equal(SandboxValue.FromInt32(1), list.Values[0]);
        Assert.Equal(SandboxValue.FromInt32(2), list.Values[1]);
    }
}
