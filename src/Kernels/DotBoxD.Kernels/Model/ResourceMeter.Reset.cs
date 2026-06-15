using System.Diagnostics;

namespace DotBoxD.Kernels;

public sealed partial class ResourceMeter
{
    internal void ResetForReuse()
    {
        _callsByBinding?.Clear();
        _deadline = CreateDeadline(Limits);
        _chargesSinceDeadlineCheck = 0;
        FuelUsed = 0;
        LoopIterations = 0;
        AllocatedBytes = 0;
        HostCalls = 0;
        FileBytesRead = 0;
        FileBytesWritten = 0;
        NetworkBytesRead = 0;
        NetworkBytesWritten = 0;
        LogEvents = 0;
        CollectionElements = 0;
        StringBytes = 0;
    }

    private static long CreateDeadline(ResourceLimits limits)
    {
        var now = Stopwatch.GetTimestamp();
        var timeoutTicks = Math.Ceiling(limits.EffectiveWallTime.TotalSeconds * Stopwatch.Frequency);
        var cappedTicks = timeoutTicks >= long.MaxValue - now
            ? long.MaxValue - now
            : (long)timeoutTicks;
        return now + Math.Max(1, cappedTicks);
    }
}
