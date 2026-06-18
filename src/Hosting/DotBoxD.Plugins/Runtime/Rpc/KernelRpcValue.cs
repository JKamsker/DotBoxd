namespace DotBoxD.Plugins;

/// <summary>The compact, transport-neutral value kinds used for server extension IPC payloads.</summary>
public enum KernelRpcValueKind : byte
{
    Unit = 0,
    Bool = 1,
    I32 = 2,
    I64 = 3,
    F64 = 4,
    String = 5,
    List = 6,
    Record = 7
}

/// <summary>
/// A compact intermediate representation for plugin-defined server extension arguments and results. Scalars
/// carry only their active field, and lists/records carry positional child values matching the verified
/// kernel IR type.
/// </summary>
public readonly struct KernelRpcValue
{
    private static readonly KernelRpcValue[] EmptyItems = [];
    private readonly KernelRpcValue[] _items;

    private KernelRpcValue(
        KernelRpcValueKind kind,
        long integerValue,
        double floatValue,
        string stringValue,
        KernelRpcValue[] items)
    {
        Kind = kind;
        IntegerValue = integerValue;
        FloatValue = floatValue;
        StringValue = stringValue;
        _items = items;
    }

    public KernelRpcValueKind Kind { get; }

    internal long IntegerValue { get; }

    internal double FloatValue { get; }

    internal string StringValue { get; }

    public KernelRpcValue[] Items => CopyItems(_items ?? EmptyItems);

    internal ReadOnlySpan<KernelRpcValue> ItemSpan => _items ?? EmptyItems;

    public bool BoolValue
    {
        get
        {
            RequireKind(KernelRpcValueKind.Bool);
            return IntegerValue != 0;
        }
    }

    public int Int32Value
    {
        get
        {
            RequireKind(KernelRpcValueKind.I32);
            return checked((int)IntegerValue);
        }
    }

    public long Int64Value
    {
        get
        {
            RequireKind(KernelRpcValueKind.I64);
            return IntegerValue;
        }
    }

    public double DoubleValue
    {
        get
        {
            RequireKind(KernelRpcValueKind.F64);
            return FloatValue;
        }
    }

    public string TextValue
    {
        get
        {
            RequireKind(KernelRpcValueKind.String);
            return StringValue;
        }
    }

    public static KernelRpcValue Unit()
        => new(KernelRpcValueKind.Unit, 0L, 0D, string.Empty, EmptyItems);

    public static KernelRpcValue Bool(bool value)
        => new(KernelRpcValueKind.Bool, value ? 1L : 0L, 0D, string.Empty, EmptyItems);

    public static KernelRpcValue Int32(int value)
        => new(KernelRpcValueKind.I32, value, 0D, string.Empty, EmptyItems);

    public static KernelRpcValue Int64(long value)
        => new(KernelRpcValueKind.I64, value, 0D, string.Empty, EmptyItems);

    public static KernelRpcValue Double(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Kernel RPC F64 values must be finite.");
        }

        return new KernelRpcValue(KernelRpcValueKind.F64, 0L, value, string.Empty, EmptyItems);
    }

    public static KernelRpcValue String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new KernelRpcValue(KernelRpcValueKind.String, 0L, 0D, value, EmptyItems);
    }

    public static KernelRpcValue List(KernelRpcValue[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new KernelRpcValue(KernelRpcValueKind.List, 0L, 0D, string.Empty, CopyItems(items));
    }

    public static KernelRpcValue Record(KernelRpcValue[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new KernelRpcValue(KernelRpcValueKind.Record, 0L, 0D, string.Empty, CopyItems(fields));
    }

    internal static KernelRpcValue ListFromOwnedItems(KernelRpcValue[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new KernelRpcValue(KernelRpcValueKind.List, 0L, 0D, string.Empty, UseOwnedItems(items));
    }

    internal static KernelRpcValue RecordFromOwnedFields(KernelRpcValue[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new KernelRpcValue(KernelRpcValueKind.Record, 0L, 0D, string.Empty, UseOwnedItems(fields));
    }

    private static KernelRpcValue[] CopyItems(KernelRpcValue[] items)
        => items.Length == 0
            ? EmptyItems
            : (KernelRpcValue[])items.Clone();

    private static KernelRpcValue[] UseOwnedItems(KernelRpcValue[] items)
        => items.Length == 0 ? EmptyItems : items;

    public void RequireKind(KernelRpcValueKind expected)
    {
        if (Kind != expected)
        {
            throw new NotSupportedException(
                $"Server extension value expected '{expected}' but received '{Kind}'.");
        }
    }
}
