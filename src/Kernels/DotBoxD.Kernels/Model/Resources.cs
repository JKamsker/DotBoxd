using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
namespace DotBoxD.Kernels.Model;

public sealed partial class ResourceMeter
{
    private const int FuelDeadlineCheckInterval = 64;
    private const int LoopDeadlineCheckInterval = 4096;
    private Dictionary<string, int>? _callsByBinding;
    private string? _lastLimitedBindingId;
    private int _lastLimitedBindingCalls;
    private long _deadline;
    private int _chargesSinceDeadlineCheck;
    public ResourceMeter(ResourceLimits limits)
    {
        ResourceLimitValidation.Validate(limits);
        Limits = limits;
        _deadline = CreateDeadline(limits);
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ChargeFuel(long amount)
        => ChargeFuel(amount, FuelDeadlineCheckInterval);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ChargeFuel(long amount, int deadlineCheckInterval)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
        FuelUsed = AddNonNegativeChecked(FuelUsed, amount, "fuel exhausted");
        if (FuelUsed > Limits.MaxFuel)
        {
            throw Quota("fuel exhausted");
        }
        if (++_chargesSinceDeadlineCheck >= deadlineCheckInterval)
        {
            _chargesSinceDeadlineCheck = 0;
            CheckDeadline();
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ChargeLoopIteration(long fuelAmount)
    {
        if (fuelAmount <= 0)
        { throw new ArgumentOutOfRangeException(nameof(fuelAmount)); }
        LoopIterations = AddNonNegativeChecked(LoopIterations, 1, "loop iteration budget exhausted");
        if (LoopIterations > Limits.MaxLoopIterations)
        {
            throw Quota("loop iteration budget exhausted");
        }
        ChargeFuel(fuelAmount, LoopDeadlineCheckInterval);
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
    {
        if (value is RecordValue && ValueShapeCache.TryGet(value, out var cachedRecordShape))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChargeMeasuredShape(cachedRecordShape);
            return;
        }

        if (TryChargeFlatScalarValue(value, cancellationToken))
        {
            return;
        }

        ChargeMeasuredShape(ValueShapeCache.GetOrMeasure(value, cancellationToken, this));
    }

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

    private static long AddNonNegativeChecked(long current, long amount, string quotaMessage)
    {
        var result = unchecked(current + amount);
        return result < current ? throw Quota(quotaMessage) : result;
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

    private static long MultiplyChecked(long left, long right, string quotaMessage)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            throw Quota(quotaMessage);
        }
    }

}
