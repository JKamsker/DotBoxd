using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0046: when a reusing <c>ShouldHandle</c> hands its input to a separate
/// <c>Handle</c> execution, <see cref="InstalledKernel.SnapshotInput"/> must hand over a deep copy. A
/// shallow wrapper would still share the kernel's reused backing array, so a subsequent same-shaped
/// dispatch overwriting that array in place would corrupt any reference the second execution retained
/// (e.g. via escaped async). The snapshot must stay frozen at the values it was taken with.
/// </summary>
public sealed class Fix_PAL_0046_Tests
{
    [Fact]
    public void SnapshotInput_is_isolated_from_later_in_place_buffer_reuse()
    {
        // The kernel reuses one owned buffer/list across dispatches: RentBuffer returns the same array
        // for a same-shaped event and CopySandboxValues overwrites its slots in place.
        var buffer = new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) };
        var reused = ListValue.FromOwnedValues(buffer, SandboxType.I32);

        var snapshot = (ListValue)InstalledKernel.SnapshotInput(reused);

        // Simulate the next same-shaped dispatch overwriting the shared buffer in place.
        buffer[0] = SandboxValue.FromInt32(99);
        buffer[1] = SandboxValue.FromInt32(99);
        reused.ResetOwnedValues(buffer);

        Assert.Equal(SandboxValue.FromInt32(99), reused[0]);
        Assert.Equal(SandboxValue.FromInt32(1), snapshot[0]);
        Assert.Equal(SandboxValue.FromInt32(2), snapshot[1]);
    }

    [Fact]
    public void SnapshotInput_passes_through_non_list_values()
    {
        var scalar = SandboxValue.FromInt32(7);

        Assert.Same(scalar, InstalledKernel.SnapshotInput(scalar));
    }
}
