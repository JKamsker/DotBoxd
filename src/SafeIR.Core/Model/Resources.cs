using System.Diagnostics;

namespace SafeIR;

public sealed class ResourceMeter
{
    private readonly Dictionary<string, int> _callsByBinding = new(StringComparer.Ordinal);
    private readonly long _deadline;
    private int _chargesSinceDeadlineCheck;

    public ResourceMeter(ResourceLimits limits)
    {
        ResourceLimitValidation.Validate(limits);
        Limits = limits;
        var now = Stopwatch.GetTimestamp();
        var timeoutTicks = Math.Ceiling(limits.EffectiveWallTime.TotalSeconds * Stopwatch.Frequency);
        var cappedTicks = timeoutTicks >= long.MaxValue - now
            ? long.MaxValue - now
            : (long)timeoutTicks;
        _deadline = now + Math.Max(1, cappedTicks);
    }

    public ResourceLimits Limits { get; }
    public long FuelUsed { get; private set; }
    public long LoopIterations { get; private set; }
    public long AllocatedBytes { get; private set; }
    public int HostCalls { get; private set; }
    public long FileBytesRead { get; private set; }
    public long FileBytesWritten { get; private set; }
    public long NetworkBytesRead { get; private set; }
    public long NetworkBytesWritten { get; private set; }
    public int LogEvents { get; private set; }
    public long CollectionElements { get; private set; }
    public long StringBytes { get; private set; }

    public SandboxResourceUsage Snapshot()
        => new(
            FuelUsed,
            Limits.MaxFuel,
            LoopIterations,
            AllocatedBytes,
            HostCalls,
            FileBytesRead,
            FileBytesWritten,
            NetworkBytesRead,
            NetworkBytesWritten,
            LogEvents,
            CollectionElements,
            StringBytes);

    public void ChargeFuel(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        FuelUsed = AddChecked(FuelUsed, amount, "fuel exhausted");
        if (FuelUsed > Limits.MaxFuel)
        {
            throw Quota("fuel exhausted");
        }

        if (++_chargesSinceDeadlineCheck >= 64)
        {
            _chargesSinceDeadlineCheck = 0;
            CheckDeadline();
        }
    }

    public void ChargeLoopIteration(long fuelAmount)
    {
        if (fuelAmount <= 0) { throw new ArgumentOutOfRangeException(nameof(fuelAmount)); }

        LoopIterations = AddChecked(LoopIterations, 1, "loop iteration budget exhausted");
        if (LoopIterations > Limits.MaxLoopIterations)
        {
            throw Quota("loop iteration budget exhausted");
        }

        ChargeFuel(fuelAmount);
    }

    public void ChargeAllocation(long bytes)
    {
        ThrowIfNegative(bytes, nameof(bytes));
        AllocatedBytes = AddChecked(AllocatedBytes, bytes, "allocation budget exhausted");
        if (AllocatedBytes > Limits.MaxAllocatedBytes)
        {
            throw Quota("allocation budget exhausted");
        }
    }

    public void ChargeCollection(SandboxValue value) => ChargeCollection(value, CancellationToken.None);

    public void ChargeCollection(SandboxValue value, CancellationToken cancellationToken)
        => ChargeMeasuredShape(SandboxValueShapeMeter.Measure(value, Limits, cancellationToken, this));

    public void ChargeValue(SandboxValue value) => ChargeValue(value, CancellationToken.None);

    public void ChargeValue(SandboxValue value, CancellationToken cancellationToken)
        => ChargeMeasuredShape(SandboxValueShapeMeter.Measure(value, Limits, cancellationToken, this));

    internal void ChargeValueShape(ValueShape shape) => ChargeMeasuredShape(shape);

    public void ChargeString(string value)
    {
        var bytes = SandboxLiteralConstraints.StringByteCount(value.Length);
        ChargeStringShape(new ValueShape(0, 0, 0, 0, value.Length, bytes));
    }

    public void ChargeStringAllocation(int charLength)
    {
        if (charLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(charLength));
        }

        var bytes = SandboxLiteralConstraints.StringByteCount(charLength);
        ChargeStringShape(new ValueShape(0, 0, 0, 0, charLength, bytes));
    }

    public void ChargeHostCall(string bindingId, int? maxCallsPerRun = null)
    {
        HostCalls = AddChecked(HostCalls, 1, $"host call budget exhausted at {bindingId}");
        if (HostCalls > Limits.MaxHostCalls)
        {
            throw Quota($"host call budget exhausted at {bindingId}");
        }

        var bindingCalls = _callsByBinding.TryGetValue(bindingId, out var existing)
            ? AddChecked(existing, 1, $"binding call budget exhausted at {bindingId}")
            : 1;
        _callsByBinding[bindingId] = bindingCalls;
        if (maxCallsPerRun is not null && bindingCalls > maxCallsPerRun.Value)
        {
            throw Quota($"binding call budget exhausted at {bindingId}");
        }
    }

    public void ChargeFileRead(long bytes)
    {
        ThrowIfNegative(bytes, nameof(bytes));
        FileBytesRead = AddChecked(FileBytesRead, bytes, "file read byte budget exhausted");
        if (FileBytesRead > Limits.MaxFileBytesRead)
        {
            throw Quota("file read byte budget exhausted");
        }
    }

    public void ChargeFileWrite(long bytes)
    {
        ThrowIfNegative(bytes, nameof(bytes));
        FileBytesWritten = AddChecked(FileBytesWritten, bytes, "file write byte budget exhausted");
        if (FileBytesWritten > Limits.MaxFileBytesWritten)
        {
            throw Quota("file write byte budget exhausted");
        }
    }

    public void ChargeNetworkRead(long bytes)
    {
        ThrowIfNegative(bytes, nameof(bytes));
        NetworkBytesRead = AddChecked(NetworkBytesRead, bytes, "network read byte budget exhausted");
        if (NetworkBytesRead > Limits.MaxNetworkBytesRead)
        {
            throw Quota("network read byte budget exhausted");
        }
    }

    public void ChargeNetworkWrite(long bytes)
    {
        ThrowIfNegative(bytes, nameof(bytes));
        NetworkBytesWritten = AddChecked(NetworkBytesWritten, bytes, "network write byte budget exhausted");
        if (NetworkBytesWritten > Limits.MaxNetworkBytesWritten)
        {
            throw Quota("network write byte budget exhausted");
        }
    }

    public void ChargeLogEvent(string message)
    {
        if (message.Length > Limits.MaxLogMessageLength)
        {
            throw Quota("log message length budget exhausted");
        }

        LogEvents = AddChecked(LogEvents, 1, "log event budget exhausted");
        if (LogEvents > Limits.MaxLogEvents)
        {
            throw Quota("log event budget exhausted");
        }
    }

    public void CheckDeadline()
    {
        if (Stopwatch.GetTimestamp() >= _deadline)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.Timeout, "wall-time budget exhausted"));
        }
    }

    public TimeSpan RemainingWallTime()
    {
        var stopwatchTicks = _deadline - Stopwatch.GetTimestamp();
        if (stopwatchTicks <= 0)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.Timeout, "wall-time budget exhausted"));
        }

        var timespanTicks = Math.Ceiling(stopwatchTicks / (double)Stopwatch.Frequency * TimeSpan.TicksPerSecond);
        if (timespanTicks >= TimeSpan.MaxValue.Ticks)
        {
            return TimeSpan.MaxValue;
        }

        return TimeSpan.FromTicks(Math.Max(1L, (long)timespanTicks));
    }

    private static SandboxRuntimeException Quota(string message)
        => new(new SandboxError(SandboxErrorCode.QuotaExceeded, message));

    private static void ThrowIfNegative(long amount, string paramName)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    private void ChargeStringShape(ValueShape shape)
    {
        if (shape.MaxStringLength > Limits.MaxStringLength)
        {
            throw Quota("string length budget exhausted");
        }

        if (shape.StringBytes > 0)
        {
            ChargeAllocation(shape.StringBytes);
        }

        StringBytes = AddChecked(StringBytes, shape.StringBytes, "string byte budget exhausted");
        if (StringBytes > Limits.MaxTotalStringBytes)
        {
            throw Quota("string byte budget exhausted");
        }
    }

    private void ChargeMeasuredShape(ValueShape shape)
    {
        if (shape.MaxListLength > Limits.MaxListLength)
        {
            throw Quota("list length budget exhausted");
        }

        if (shape.MaxMapEntries > Limits.MaxMapEntries)
        {
            throw Quota("map entry budget exhausted");
        }

        if (shape.Depth > Limits.MaxCollectionDepth)
        {
            throw Quota("collection depth budget exhausted");
        }

        CollectionElements = AddChecked(CollectionElements, shape.Elements, "collection element budget exhausted");
        if (CollectionElements > Limits.MaxTotalCollectionElements)
        {
            throw Quota("collection element budget exhausted");
        }

        ChargeStringShape(shape);
    }

    private static long AddChecked(long current, long amount, string quotaMessage)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw Quota(quotaMessage);
        }
    }

    private static int AddChecked(int current, int amount, string quotaMessage)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw Quota(quotaMessage);
        }
    }

}
