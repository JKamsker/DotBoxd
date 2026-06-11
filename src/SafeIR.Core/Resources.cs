using System.Diagnostics;

namespace SafeIR;

public sealed record ResourceLimits(
    long MaxFuel = 100_000,
    TimeSpan? MaxWallTime = null,
    long MaxAllocatedBytes = 1_048_576,
    int MaxCallDepth = 64,
    int MaxHostCalls = 100,
    long MaxFileBytesRead = 1_048_576,
    long MaxFileBytesWritten = 0,
    long MaxNetworkBytesRead = 1_048_576,
    int MaxLogEvents = 100)
{
    public TimeSpan EffectiveWallTime => MaxWallTime ?? TimeSpan.FromMilliseconds(100);
}

public sealed class ResourceMeter
{
    private readonly long _deadline;
    private int _chargesSinceDeadlineCheck;

    public ResourceMeter(ResourceLimits limits)
    {
        Limits = limits;
        _deadline = Stopwatch.GetTimestamp() + (long)(limits.EffectiveWallTime.TotalSeconds * Stopwatch.Frequency);
    }

    public ResourceLimits Limits { get; }
    public long FuelUsed { get; private set; }
    public long AllocatedBytes { get; private set; }
    public int HostCalls { get; private set; }
    public long FileBytesRead { get; private set; }
    public long FileBytesWritten { get; private set; }
    public long NetworkBytesRead { get; private set; }
    public int LogEvents { get; private set; }

    public SandboxResourceUsage Snapshot()
        => new(FuelUsed, Limits.MaxFuel, AllocatedBytes, HostCalls, FileBytesRead, FileBytesWritten, NetworkBytesRead, LogEvents);

    public void ChargeFuel(long amount)
    {
        if (amount < 0) {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        FuelUsed += amount;
        if (FuelUsed > Limits.MaxFuel) {
            throw Quota("fuel exhausted");
        }

        if (++_chargesSinceDeadlineCheck >= 64) {
            _chargesSinceDeadlineCheck = 0;
            CheckDeadline();
        }
    }

    public void ChargeAllocation(long bytes)
    {
        AllocatedBytes += bytes;
        if (AllocatedBytes > Limits.MaxAllocatedBytes) {
            throw Quota("allocation budget exhausted");
        }
    }

    public void ChargeHostCall(string bindingId)
    {
        HostCalls++;
        if (HostCalls > Limits.MaxHostCalls) {
            throw Quota($"host call budget exhausted at {bindingId}");
        }
    }

    public void ChargeFileRead(long bytes)
    {
        FileBytesRead += bytes;
        if (FileBytesRead > Limits.MaxFileBytesRead) {
            throw Quota("file read byte budget exhausted");
        }
    }

    public void ChargeFileWrite(long bytes)
    {
        FileBytesWritten += bytes;
        if (FileBytesWritten > Limits.MaxFileBytesWritten) {
            throw Quota("file write byte budget exhausted");
        }
    }

    public void ChargeNetworkRead(long bytes)
    {
        NetworkBytesRead += bytes;
        if (NetworkBytesRead > Limits.MaxNetworkBytesRead) {
            throw Quota("network read byte budget exhausted");
        }
    }

    public void CheckDeadline()
    {
        if (Stopwatch.GetTimestamp() > _deadline) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.Timeout, "wall-time budget exhausted"));
        }
    }

    private static SandboxRuntimeException Quota(string message)
        => new(new SandboxError(SandboxErrorCode.QuotaExceeded, message));
}

public sealed record SandboxResourceUsage(
    long FuelUsed,
    long MaxFuel,
    long AllocatedBytes,
    int HostCalls,
    long FileBytesRead,
    long FileBytesWritten,
    long NetworkBytesRead,
    int LogEvents);
