using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

public sealed class Fix_PAL_0013_Tests
{
    [Fact]
    public void Create_value_array_returns_shared_empty_singleton_for_zero_arguments()
    {
        // Zero-argument compiled binding calls must not allocate a fresh
        // SandboxValue[] per dispatch. The emitter never stores into a
        // zero-length argument array, so the immutable shared empty singleton
        // is returned instead of a new heap array.
        var context = Context();

        var array = CompiledRuntime.CreateValueArray(context, 0);

        Assert.Empty(array);
        Assert.Same(Array.Empty<SandboxValue>(), array);
    }

    [Fact]
    public void Create_value_array_for_zero_arguments_does_not_allocate_per_call()
    {
        var context = Context();

        CompiledRuntime.CreateValueArray(context, 0); // warm up first-call JIT allocations

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            _ = CompiledRuntime.CreateValueArray(context, 0);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Create_value_array_for_zero_arguments_charges_fuel_and_allocation_unchanged()
    {
        // Resource accounting must stay identical to allocating the array: a
        // zero-length request still charges one fuel unit and eight allocation
        // bytes (Math.Max(1, count) * 8), even though no heap array is created.
        var context = Context();

        CompiledRuntime.CreateValueArray(context, 0);

        Assert.Equal(1, context.Budget.FuelUsed);
        Assert.Equal(8, context.Budget.AllocatedBytes);
    }

    [Fact]
    public void Create_value_array_still_allocates_distinct_arrays_for_nonzero_arguments()
    {
        var context = Context();

        var first = CompiledRuntime.CreateValueArray(context, 2);
        var second = CompiledRuntime.CreateValueArray(context, 2);

        Assert.Equal(2, first.Length);
        Assert.Equal(2, second.Length);
        Assert.NotSame(first, second);
    }

    private static SandboxContext Context()
    {
        var limits = new ResourceLimits(MaxFuel: 1_000_000, MaxAllocatedBytes: 1_000_000);
        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }
}
