using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class CompiledRuntimeQuotaTests
{
    [Fact]
    public void Create_value_array_charges_large_counts_without_integer_overflow()
    {
        var context = ContextWithLimits(new ResourceLimits(MaxFuel: 1_000_000_000, MaxAllocatedBytes: 16));

        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            CompiledRuntime.CreateValueArray(context, 536_870_912));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
        Assert.Equal(536_870_912, context.Budget.FuelUsed);
        Assert.True(context.Budget.AllocatedBytes > context.Budget.Limits.MaxAllocatedBytes);
    }

    private static SandboxContext ContextWithLimits(ResourceLimits limits)
        => new(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
}
