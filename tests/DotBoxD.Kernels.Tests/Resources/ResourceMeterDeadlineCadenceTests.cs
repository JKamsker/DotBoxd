using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Resources;

public sealed class ResourceMeterDeadlineCadenceTests
{
    [Fact]
    public void Fuel_charges_check_expired_deadlines_at_fuel_cadence()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxFuel: long.MaxValue, MaxWallTime: TimeSpan.Zero));

        for (var i = 0; i < 63; i++)
        {
            meter.ChargeFuel(1);
        }

        var ex = Assert.Throws<SandboxRuntimeException>(() => meter.ChargeFuel(1));
        Assert.Equal(SandboxErrorCode.Timeout, ex.Error.Code);
    }

    [Fact]
    public void Loop_iteration_charges_keep_loop_deadline_cadence()
    {
        var meter = new ResourceMeter(new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxLoopIterations: long.MaxValue,
            MaxWallTime: TimeSpan.Zero));

        for (var i = 0; i < 4095; i++)
        {
            meter.ChargeLoopIteration(1);
        }

        var ex = Assert.Throws<SandboxRuntimeException>(() => meter.ChargeLoopIteration(1));
        Assert.Equal(SandboxErrorCode.Timeout, ex.Error.Code);
    }
}
