using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Resources;

public sealed class ResourceMeterTests
{
    [Theory]
    [MemberData(nameof(NegativeByteCharges))]
    public void Resource_meter_rejects_negative_byte_charges(Action<ResourceMeter> charge)
    {
        var meter = new ResourceMeter(new ResourceLimits());

        Assert.Throws<ArgumentOutOfRangeException>(() => charge(meter));

        Assert.Equal(0, meter.AllocatedBytes);
        Assert.Equal(0, meter.FileBytesRead);
        Assert.Equal(0, meter.FileBytesWritten);
        Assert.Equal(0, meter.NetworkBytesRead);
        Assert.Equal(0, meter.NetworkBytesWritten);
    }

    public static TheoryData<Action<ResourceMeter>> NegativeByteCharges()
        => new() {
            meter => meter.ChargeAllocation(-1),
            meter => meter.ChargeFileRead(-1),
            meter => meter.ChargeFileWrite(-1),
            meter => meter.ChargeNetworkRead(-1),
            meter => meter.ChargeNetworkWrite(-1)
        };

    [Fact]
    public void Resource_meter_enforces_network_write_budget()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxNetworkBytesWritten: 3));

        meter.ChargeNetworkWrite(3);

        Assert.Equal(3, meter.NetworkBytesWritten);
        Assert.Throws<SandboxRuntimeException>(() => meter.ChargeNetworkWrite(1));
    }

    [Fact]
    public void Resource_meter_enforces_loop_iteration_budget()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxLoopIterations: 2, MaxFuel: 100));

        meter.ChargeLoopIteration(5);
        meter.ChargeLoopIteration(5);

        var ex = Assert.Throws<SandboxRuntimeException>(() => meter.ChargeLoopIteration(5));
        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
        Assert.Equal(3, meter.LoopIterations);
        Assert.Equal(10, meter.FuelUsed);
    }

    [Fact]
    public void Resource_meter_rejects_non_positive_loop_fuel_before_charging_iteration()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxLoopIterations: 2, MaxFuel: 100));

        Assert.Throws<ArgumentOutOfRangeException>(() => meter.ChargeLoopIteration(0));

        Assert.Equal(0, meter.LoopIterations);
        Assert.Equal(0, meter.FuelUsed);
    }

    [Fact]
    public void Remaining_wall_time_handles_large_valid_budgets()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxWallTime: TimeSpan.FromDays(2)));

        var remaining = meter.RemainingWallTime();

        Assert.True(remaining > TimeSpan.FromDays(1));
    }

    [Fact]
    public void Resource_limits_reject_wall_time_above_supported_cancel_after_range()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ResourceLimitValidation.Validate(new ResourceLimits(MaxWallTime: TimeSpan.MaxValue)));

        Assert.Equal(nameof(ResourceLimits.MaxWallTime), ex.ParamName);
    }

    [Fact]
    public void Shape_scan_charges_fuel_for_large_collections()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxFuel: 0));
        var values = Enumerable.Range(0, 128)
            .Select(SandboxValue.FromInt32)
            .ToArray();
        var value = SandboxValue.FromList(values);

        var ex = Assert.Throws<SandboxRuntimeException>(() => meter.ChargeValue(value));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
    }

    [Fact]
    public void Shape_cache_miss_checks_wall_time_during_measurement()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxWallTime: TimeSpan.Zero));
        var value = CreateFlatScalarList(128);

        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            SandboxValueShapeMeter.MeasureWithNodes(value, CancellationToken.None, meter));

        Assert.Equal(SandboxErrorCode.Timeout, ex.Error.Code);
    }

    [Fact]
    public void Cached_shape_charge_checks_wall_time_after_aggregated_scan_fuel()
    {
        var value = CreateFlatScalarList(128);
        _ = ValueShapeCache.GetOrMeasure(value);
        var meter = new ResourceMeter(new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxWallTime: TimeSpan.Zero));

        var ex = Assert.Throws<SandboxRuntimeException>(() => meter.ChargeValue(value));

        Assert.Equal(SandboxErrorCode.Timeout, ex.Error.Code);
    }

    [Fact]
    public void Flat_scalar_list_fast_path_matches_shape_scan_fuel_boundary()
    {
        var freeMeter = new ResourceMeter(new ResourceLimits(MaxFuel: 0));
        var freeValue = CreateFlatScalarList(61);

        freeMeter.ChargeValue(freeValue);

        var chargedMeter = new ResourceMeter(new ResourceLimits(MaxFuel: 0));
        var chargedValue = CreateFlatScalarList(62);
        var ex = Assert.Throws<SandboxRuntimeException>(() => chargedMeter.ChargeValue(chargedValue));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
    }

    [Fact]
    public void Flat_scalar_list_charge_preserves_resource_usage()
    {
        var meter = new ResourceMeter(new ResourceLimits());
        var value = SandboxValue.FromList(
            [
                SandboxValue.FromInt32(1),
                SandboxValue.FromString("ab"),
                SandboxValue.FromBool(true)
            ],
            SandboxType.I32);

        meter.ChargeValue(value);

        Assert.Equal(3, meter.CollectionElements);
        Assert.Equal(4, meter.StringBytes);
        Assert.Equal(4, meter.AllocatedBytes);
    }

    [Fact]
    public void Flat_scalar_list_charge_preserves_quota_failures()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxListLength: 2));
        var value = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3)],
            SandboxType.I32);

        var ex = Assert.Throws<SandboxRuntimeException>(() => meter.ChargeValue(value));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
    }

    [Fact]
    public void Flat_scalar_record_fast_path_matches_shape_scan_fuel_boundary()
    {
        var freeMeter = new ResourceMeter(new ResourceLimits(MaxFuel: 0));
        var freeValue = CreateFlatScalarRecord(61);

        freeMeter.ChargeValue(freeValue);

        var chargedMeter = new ResourceMeter(new ResourceLimits(MaxFuel: 0));
        var chargedValue = CreateFlatScalarRecord(62);
        var ex = Assert.Throws<SandboxRuntimeException>(() => chargedMeter.ChargeValue(chargedValue));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
    }

    [Fact]
    public void Flat_scalar_record_charge_preserves_resource_usage()
    {
        var meter = new ResourceMeter(new ResourceLimits());
        var value = SandboxValue.FromRecord([
            SandboxValue.FromInt32(1),
            SandboxValue.FromString("ab"),
            SandboxValue.FromBool(true)
        ]);

        meter.ChargeValue(value);

        Assert.Equal(3, meter.CollectionElements);
        Assert.Equal(4, meter.StringBytes);
        Assert.Equal(4, meter.AllocatedBytes);
    }

    [Fact]
    public void Flat_scalar_record_charge_preserves_quota_failures()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxListLength: 2));
        var value = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3)]);

        var ex = Assert.Throws<SandboxRuntimeException>(() => meter.ChargeValue(value));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
    }

    [Fact]
    public void ResetForReuse_clears_usage_and_per_binding_counts()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxHostCalls: 2));
        meter.ChargeValue(SandboxValue.FromString("fire"));
        meter.ChargeHostCall("test.binding", maxCallsPerRun: 1);

        meter.ResetForReuse();

        var usage = meter.Snapshot();
        Assert.Equal(0, usage.HostCalls);
        Assert.Equal(0, usage.StringBytes);
        Assert.Equal(0, usage.AllocatedBytes);
        meter.ChargeHostCall("test.binding", maxCallsPerRun: 1);
    }

    [Fact]
    public void Resource_meter_enforces_per_binding_limit()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxHostCalls: 10));

        meter.ChargeHostCall("test.binding", maxCallsPerRun: 1);

        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            meter.ChargeHostCall("test.binding", maxCallsPerRun: 1));
        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
        Assert.Equal(2, meter.HostCalls);
    }

    [Fact]
    public void Resource_meter_enforces_per_binding_limit_after_switching_bindings()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxHostCalls: 10));

        meter.ChargeHostCall("test.binding.a", maxCallsPerRun: 1);
        meter.ChargeHostCall("test.binding.b", maxCallsPerRun: 1);

        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            meter.ChargeHostCall("test.binding.a", maxCallsPerRun: 1));
        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
        Assert.Equal(3, meter.HostCalls);
    }

    [Fact]
    public void Resource_meter_unlimited_host_calls_track_global_limit()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxHostCalls: 2));

        meter.ChargeHostCall("test.binding");
        meter.ChargeHostCall("test.binding");

        var ex = Assert.Throws<SandboxRuntimeException>(() => meter.ChargeHostCall("test.binding"));
        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
        Assert.Equal(3, meter.HostCalls);
    }

    private static SandboxValue CreateFlatScalarList(int count)
        => SandboxValue.FromList(
            Enumerable.Range(0, count)
                .Select(SandboxValue.FromInt32)
                .ToArray(),
            SandboxType.I32);

    private static SandboxValue CreateFlatScalarRecord(int count)
        => SandboxValue.FromRecord(
            Enumerable.Range(0, count)
                .Select(SandboxValue.FromInt32)
                .ToArray());
}
