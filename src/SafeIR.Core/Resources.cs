using System.Diagnostics;

namespace SafeIR;

public sealed record ResourceLimits(
    long MaxFuel = 100_000,
    TimeSpan? MaxWallTime = null,
    long MaxAllocatedBytes = 1_048_576,
    int MaxCallDepth = 64,
    int MaxHostCalls = 100,
    int MaxListLength = 10_000,
    int MaxMapEntries = 10_000,
    int MaxCollectionDepth = 32,
    long MaxTotalCollectionElements = 100_000,
    long MaxFileBytesRead = 1_048_576,
    long MaxFileBytesWritten = 0,
    long MaxNetworkBytesRead = 1_048_576,
    int MaxLogEvents = 100,
    int MaxLogMessageLength = 4_096,
    int MaxStringLength = 65_536,
    long MaxTotalStringBytes = 1_048_576)
{
    public TimeSpan EffectiveWallTime => MaxWallTime ?? TimeSpan.FromMilliseconds(100);
}

public sealed class ResourceMeter
{
    private readonly Dictionary<string, int> _callsByBinding = new(StringComparer.Ordinal);
    private readonly long _deadline;
    private int _chargesSinceDeadlineCheck;

    public ResourceMeter(ResourceLimits limits)
    {
        ResourceLimitValidation.Validate(limits);
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
    public long CollectionElements { get; private set; }
    public long StringBytes { get; private set; }

    public SandboxResourceUsage Snapshot()
        => new(
            FuelUsed,
            Limits.MaxFuel,
            AllocatedBytes,
            HostCalls,
            FileBytesRead,
            FileBytesWritten,
            NetworkBytesRead,
            LogEvents,
            CollectionElements,
            StringBytes);

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
        ThrowIfNegative(bytes, nameof(bytes));
        AllocatedBytes += bytes;
        if (AllocatedBytes > Limits.MaxAllocatedBytes) {
            throw Quota("allocation budget exhausted");
        }
    }

    public void ChargeCollection(SandboxValue value)
    {
        var shape = MeasureValue(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
        if (shape.MaxListLength > Limits.MaxListLength) {
            throw Quota("list length budget exhausted");
        }

        if (shape.MaxMapEntries > Limits.MaxMapEntries) {
            throw Quota("map entry budget exhausted");
        }

        if (shape.Depth > Limits.MaxCollectionDepth) {
            throw Quota("collection depth budget exhausted");
        }

        CollectionElements += shape.Elements;
        if (CollectionElements > Limits.MaxTotalCollectionElements) {
            throw Quota("collection element budget exhausted");
        }

        ChargeStringShape(shape);
    }

    public void ChargeValue(SandboxValue value)
    {
        var shape = MeasureValue(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
        if (shape.MaxStringLength > Limits.MaxStringLength) {
            throw Quota("string length budget exhausted");
        }

        if (shape.MaxListLength > Limits.MaxListLength) {
            throw Quota("list length budget exhausted");
        }

        if (shape.MaxMapEntries > Limits.MaxMapEntries) {
            throw Quota("map entry budget exhausted");
        }

        if (shape.Depth > Limits.MaxCollectionDepth) {
            throw Quota("collection depth budget exhausted");
        }

        CollectionElements += shape.Elements;
        if (CollectionElements > Limits.MaxTotalCollectionElements) {
            throw Quota("collection element budget exhausted");
        }

        ChargeStringShape(shape);
    }

    public void ChargeString(string value)
    {
        var bytes = value.Length * sizeof(char);
        ChargeStringShape(new ValueShape(0, 0, 0, 0, value.Length, bytes));
    }

    public void ChargeHostCall(string bindingId, int? maxCallsPerRun = null)
    {
        HostCalls++;
        if (HostCalls > Limits.MaxHostCalls) {
            throw Quota($"host call budget exhausted at {bindingId}");
        }

        var bindingCalls = _callsByBinding.TryGetValue(bindingId, out var existing) ? existing + 1 : 1;
        _callsByBinding[bindingId] = bindingCalls;
        if (maxCallsPerRun is not null && bindingCalls > maxCallsPerRun.Value) {
            throw Quota($"binding call budget exhausted at {bindingId}");
        }
    }

    public void ChargeFileRead(long bytes)
    {
        ThrowIfNegative(bytes, nameof(bytes));
        FileBytesRead += bytes;
        if (FileBytesRead > Limits.MaxFileBytesRead) {
            throw Quota("file read byte budget exhausted");
        }
    }

    public void ChargeFileWrite(long bytes)
    {
        ThrowIfNegative(bytes, nameof(bytes));
        FileBytesWritten += bytes;
        if (FileBytesWritten > Limits.MaxFileBytesWritten) {
            throw Quota("file write byte budget exhausted");
        }
    }

    public void ChargeNetworkRead(long bytes)
    {
        ThrowIfNegative(bytes, nameof(bytes));
        NetworkBytesRead += bytes;
        if (NetworkBytesRead > Limits.MaxNetworkBytesRead) {
            throw Quota("network read byte budget exhausted");
        }
    }

    public void ChargeLogEvent(string message)
    {
        if (message.Length > Limits.MaxLogMessageLength) {
            throw Quota("log message length budget exhausted");
        }

        LogEvents++;
        if (LogEvents > Limits.MaxLogEvents) {
            throw Quota("log event budget exhausted");
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

    private static void ThrowIfNegative(long amount, string paramName)
    {
        if (amount < 0) {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    private void ChargeStringShape(ValueShape shape)
    {
        if (shape.MaxStringLength > Limits.MaxStringLength) {
            throw Quota("string length budget exhausted");
        }

        if (shape.StringBytes > 0) {
            ChargeAllocation(shape.StringBytes);
        }

        StringBytes += shape.StringBytes;
        if (StringBytes > Limits.MaxTotalStringBytes) {
            throw Quota("string byte budget exhausted");
        }
    }

    private static ValueShape MeasureValue(SandboxValue value, HashSet<object> stack)
        => value switch {
            ListValue list => MeasureList(list, stack),
            MapValue map => MeasureMap(map, stack),
            StringValue text => new ValueShape(0, 0, 0, 0, text.Value.Length, text.Value.Length * sizeof(char)),
            _ => new ValueShape(0, 0, 0, 0, 0, 0)
        };

    private static ValueShape MeasureList(ListValue list, HashSet<object> stack)
    {
        Enter(list, stack);
        try {
            var shape = new ValueShape(list.Values.Count, list.Values.Count, 0, 1, 0, 0);
            foreach (var item in list.Values) {
                shape = shape.Combine(MeasureValue(item, stack));
            }

            return shape;
        }
        finally {
            stack.Remove(list);
        }
    }

    private static ValueShape MeasureMap(MapValue map, HashSet<object> stack)
    {
        Enter(map, stack);
        try {
            var shape = new ValueShape(map.Values.Count, 0, map.Values.Count, 1, 0, 0);
            foreach (var pair in map.Values) {
                shape = shape
                    .Combine(MeasureValue(pair.Key, stack))
                    .Combine(MeasureValue(pair.Value, stack));
            }

            return shape;
        }
        finally {
            stack.Remove(map);
        }
    }

    private static void Enter(object value, HashSet<object> stack)
    {
        if (!stack.Add(value)) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.InvalidInput, "cyclic collection value is not supported"));
        }
    }
}

public sealed record SandboxResourceUsage(
    long FuelUsed,
    long MaxFuel,
    long AllocatedBytes,
    int HostCalls,
    long FileBytesRead,
    long FileBytesWritten,
    long NetworkBytesRead,
    int LogEvents,
    long CollectionElements,
    long StringBytes);
