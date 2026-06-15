namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0045: the plugin input builder reuses a single <see cref="ListValue"/>
/// across dispatches by rebinding its backing array via <c>ResetOwnedValues</c>. That in-place mutation
/// is only safe on a list the builder constructed and owns (<c>FromOwnedValues</c>); rebinding a
/// normally constructed, potentially externally visible list would let a retained reference observe a
/// later dispatch's input. The guard rejects that misuse.
/// </summary>
public sealed class Fix_PAL_0045_Tests
{
    [Fact]
    public void ResetOwnedValues_rebinds_buffer_on_owned_list()
    {
        var list = ListValue.FromOwnedValues(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)],
            SandboxType.I32);

        list.ResetOwnedValues([SandboxValue.FromInt32(7), SandboxValue.FromInt32(8)]);

        Assert.Equal(2, list.Count);
        Assert.Equal(SandboxValue.FromInt32(7), list[0]);
        Assert.Equal(SandboxValue.FromInt32(8), list[1]);
    }

    [Fact]
    public void ResetOwnedValues_rejects_externally_constructed_list()
    {
        var list = new ListValue(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)],
            SandboxType.I32);

        Assert.Throws<InvalidOperationException>(
            () => list.ResetOwnedValues([SandboxValue.FromInt32(9), SandboxValue.FromInt32(9)]));
    }
}
