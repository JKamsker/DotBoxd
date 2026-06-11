namespace SafeIR.Tests;

public sealed class ResourceLimitValidationTests
{
    [Theory]
    [MemberData(nameof(NegativePolicyLimits))]
    public void Policy_builder_rejects_negative_resource_limits(
        Func<SandboxPolicyBuilder, SandboxPolicyBuilder> configure)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => configure(SandboxPolicyBuilder.Create()).Build());
    }

    [Fact]
    public void Resource_meter_rejects_negative_limits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceMeter(new ResourceLimits(MaxFuel: -1)));
    }

    public static TheoryData<Func<SandboxPolicyBuilder, SandboxPolicyBuilder>> NegativePolicyLimits()
        => new() {
            builder => builder.WithFuel(-1),
            builder => builder.WithWallTime(TimeSpan.FromTicks(-1)),
            builder => builder.WithMaxAllocatedBytes(-1),
            builder => builder.WithMaxCallDepth(-1),
            builder => builder.WithMaxHostCalls(-1),
            builder => builder.WithMaxListLength(-1),
            builder => builder.WithMaxMapEntries(-1),
            builder => builder.WithMaxCollectionDepth(-1),
            builder => builder.WithMaxTotalCollectionElements(-1),
            builder => builder.WithMaxLogEvents(-1),
            builder => builder.WithMaxLogMessageLength(-1),
            builder => builder.WithMaxStringLength(-1),
            builder => builder.WithMaxTotalStringBytes(-1),
            builder => builder.GrantFileRead("root", -1),
            builder => builder.GrantFileWrite("root", -1),
            builder => builder.GrantHttpGet(["example.test"], -1),
            builder => builder.GrantHttpGet(["example.test"], 1, timeout: TimeSpan.FromTicks(-1))
        };
}
