using SafeIR;

namespace SafeIR.Tests;

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
}
