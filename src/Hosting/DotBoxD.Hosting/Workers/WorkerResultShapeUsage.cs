using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Hosting;

internal static class WorkerResultShapeUsage
{
    public static SandboxResourceUsage Measure(
        SandboxValue value,
        SandboxType expectedType,
        ResourceLimits limits)
    {
        var meter = new ResourceMeter(limits);
        var shape = SandboxValidatedValueShapeMeter.Measure(
            value,
            expectedType,
            SandboxErrorCode.InvalidInput,
            "worker result return type mismatch",
            limits,
            meter: meter);
        meter.ChargeValueShape(shape);
        return meter.Snapshot();
    }
}
