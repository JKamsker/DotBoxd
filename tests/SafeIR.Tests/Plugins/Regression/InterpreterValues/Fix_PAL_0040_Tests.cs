using SafeIR;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0040: plugin kernel input construction allocated a
/// working <see cref="SandboxValue"/>[] for multi-value inputs and then the
/// <see cref="ListValue"/> constructor defensively snapshotted that array into a
/// second array. The owned-array construction path (<see cref="SandboxValue.FromOwnedList"/>)
/// wraps the working array exactly once. The public <see cref="SandboxValue.FromList(IReadOnlyList{SandboxValue}, SandboxType)"/>
/// factory must keep its defensive copy so external callers cannot mutate a list value.
/// </summary>
public sealed class Fix_PAL_0040_Tests
{
    [Fact]
    public void FromOwnedList_wraps_the_caller_array_without_a_second_copy()
    {
        var owned = new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) };

        var list = (ListValue)SandboxValue.FromOwnedList(owned, SandboxType.I32);

        // The owned array is wrapped, not re-copied: mutating the source the caller
        // handed off is observable through the value. If the constructor reverted to a
        // second defensive snapshot this assertion would fail, catching the regression.
        owned[0] = SandboxValue.FromInt32(99);

        Assert.Equal(2, list.Values.Count);
        Assert.Equal(SandboxValue.FromInt32(99), list.Values[0]);
        Assert.Equal(SandboxValue.FromInt32(2), list.Values[1]);
        Assert.Equal(SandboxType.List(SandboxType.I32), list.Type);
    }

    [Fact]
    public void FromList_keeps_a_defensive_copy_for_caller_supplied_arrays()
    {
        var source = new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) };

        var list = (ListValue)SandboxValue.FromList(source, SandboxType.I32);

        // Mutating the caller's array after construction must NOT affect the list value.
        source[0] = SandboxValue.FromInt32(99);

        Assert.Equal(SandboxValue.FromInt32(1), list.Values[0]);
        Assert.Equal(SandboxValue.FromInt32(2), list.Values[1]);
    }

    [Fact]
    public void Owned_and_defensive_lists_are_structurally_equal()
    {
        var owned = SandboxValue.FromOwnedList(
            new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) },
            SandboxType.I32);
        var defensive = SandboxValue.FromList(
            new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) },
            SandboxType.I32);

        Assert.Equal(defensive, owned);
        Assert.Equal(defensive.GetHashCode(), owned.GetHashCode());
    }

    [Fact]
    public void Owned_list_value_survives_record_with_copy()
    {
        var owned = (ListValue)SandboxValue.FromOwnedList(
            new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) },
            SandboxType.I32);

        var copied = owned with { };

        Assert.Equal(owned, copied);
        Assert.Equal(2, copied.Values.Count);
        Assert.Equal(SandboxValue.FromInt32(1), copied.Values[0]);
    }

    [Fact]
    public void Reassigning_Values_on_an_owned_list_still_defensively_copies()
    {
        var owned = (ListValue)SandboxValue.FromOwnedList(
            new[] { SandboxValue.FromInt32(1) },
            SandboxType.I32);

        var replacement = new[] { SandboxValue.FromInt32(5), SandboxValue.FromInt32(6) };
        var updated = owned with { Values = replacement };

        replacement[0] = SandboxValue.FromInt32(99);

        Assert.Equal(SandboxValue.FromInt32(5), updated.Values[0]);
        Assert.Equal(SandboxValue.FromInt32(6), updated.Values[1]);
    }
}
