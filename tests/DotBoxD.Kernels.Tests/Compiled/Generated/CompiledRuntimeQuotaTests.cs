using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Compiled.Generated;

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
            Kernels.Runtime.CompiledRuntime.CreateValueArray(context, 536_870_912));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
        Assert.Equal(536_870_912, context.Budget.FuelUsed);
        Assert.True(context.Budget.AllocatedBytes > context.Budget.Limits.MaxAllocatedBytes);
    }

    [Fact]
    public void ListOf_wraps_compiler_owned_value_array()
    {
        var context = ContextWithLimits(new ResourceLimits(MaxFuel: long.MaxValue, MaxAllocatedBytes: long.MaxValue));
        var values = new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) };

        var list = Assert.IsType<ListValue>(CompiledRuntime.ListOf(context, values));

        values[0] = SandboxValue.FromInt32(99);
        Assert.Equal(SandboxValue.FromInt32(99), list.Values[0]);
        Assert.Equal(SandboxType.List(SandboxType.I32), list.Type);
    }

    [Fact]
    public void RecordNew_wraps_compiler_owned_field_array()
    {
        var context = ContextWithLimits(new ResourceLimits(MaxFuel: long.MaxValue, MaxAllocatedBytes: long.MaxValue));
        var fields = new[] { SandboxValue.FromInt32(1), SandboxValue.FromString("two") };

        var record = Assert.IsType<RecordValue>(CompiledRuntime.RecordNew(context, fields));

        fields[0] = SandboxValue.FromInt32(99);
        Assert.Equal(SandboxValue.FromInt32(99), record.Fields[0]);
        Assert.Equal(SandboxType.Record([SandboxType.I32, SandboxType.String]), record.Type);
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
