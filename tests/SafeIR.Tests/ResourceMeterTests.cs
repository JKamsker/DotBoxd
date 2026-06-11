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
    }

    public static TheoryData<Action<ResourceMeter>> NegativeByteCharges()
        => new() {
            meter => meter.ChargeAllocation(-1),
            meter => meter.ChargeFileRead(-1),
            meter => meter.ChargeFileWrite(-1),
            meter => meter.ChargeNetworkRead(-1)
        };
}
