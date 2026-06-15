using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Tests;

public sealed class CompiledRuntimeQuotaTests
{
    [Theory]
    [InlineData("Unit")]
    [InlineData("Bool")]
    [InlineData("I32")]
    [InlineData("I64")]
    [InlineData("F64")]
    [InlineData("String")]
    [InlineData("SandboxPath")]
    [InlineData("SandboxUri")]
    public void Type_scalar_reuses_builtin_scalar_singletons(string name)
    {
        var type = CompiledRuntime.TypeScalar(name);

        Assert.Same(BuiltinScalar(name), type);
    }

    [Fact]
    public void Type_scalar_keeps_non_builtin_scalar_fallback()
    {
        var type = CompiledRuntime.TypeScalar("MonsterId");

        Assert.Equal(SandboxType.Scalar("MonsterId"), type);
    }

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

    private static SandboxType BuiltinScalar(string name)
        => name switch
        {
            "Unit" => SandboxType.Unit,
            "Bool" => SandboxType.Bool,
            "I32" => SandboxType.I32,
            "I64" => SandboxType.I64,
            "F64" => SandboxType.F64,
            "String" => SandboxType.String,
            "SandboxPath" => SandboxType.SandboxPath,
            "SandboxUri" => SandboxType.SandboxUri,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unexpected built-in scalar")
        };

    private static SandboxContext ContextWithLimits(ResourceLimits limits)
        => new(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
}
