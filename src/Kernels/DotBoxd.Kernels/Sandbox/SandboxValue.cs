namespace DotBoxd.Kernels;

public abstract record SandboxValue
{
    public static SandboxValue Unit { get; } = new UnitValue();

    public abstract SandboxType Type { get; }

    public static SandboxValue FromBool(bool value) => new BoolValue(value);

    public static SandboxValue FromInt32(int value) => new I32Value(value);

    public static SandboxValue FromInt64(long value) => new I64Value(value);

    public static SandboxValue FromDouble(double value)
        => double.IsFinite(value)
            ? new F64Value(value)
            : throw new ArgumentOutOfRangeException(nameof(value), value, "F64 values must be finite.");

    public static SandboxValue FromString(string value) => new StringValue(value);

    public static SandboxValue FromOpaqueId(string typeName, string value)
        => SandboxType.IsKnownOpaqueId(typeName) && SandboxLiteralConstraints.IsOpaqueId(value)
            ? new OpaqueIdValue(typeName, value)
            : throw new ArgumentException("Opaque IDs must use a well-formed brand type and a safe ID value.", nameof(value));

    public static SandboxValue FromPath(string value)
        => SandboxLiteralConstraints.IsPortableRelativePath(value)
            ? new SandboxPathValue(new SandboxPath(value))
            : throw new ArgumentException("Sandbox paths must be portable relative paths.", nameof(value));

    public static SandboxValue FromUri(string value)
        => SandboxLiteralConstraints.IsSandboxUri(value)
            ? new SandboxUriValue(new SandboxUri(value))
            : throw new ArgumentException("Sandbox URIs must be absolute and must not include user info.", nameof(value));

    public static SandboxValue FromList(IReadOnlyList<SandboxValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        // Infer the item type from the original list and take exactly one defensive
        // snapshot via the owned-array path, instead of copying once here and again in
        // the ListValue constructor.
        var itemType = values.Count == 0 ? SandboxType.Unit : values[0].Type;
        return FromOwnedList(values.ToArray(), itemType);
    }

    public static SandboxValue FromList(IReadOnlyList<SandboxValue> values, SandboxType itemType)
        => new ListValue(values, itemType);

    /// <summary>
    /// Builds a list value from an array the caller has just allocated, fully populated,
    /// and will not retain or mutate, avoiding the defensive copy that <see cref="FromList(IReadOnlyList{SandboxValue})"/>
    /// performs. Internal because the owned-array contract cannot be enforced externally.
    /// </summary>
    internal static SandboxValue FromOwnedList(SandboxValue[] values, SandboxType itemType)
        => ListValue.FromOwnedValues(values, itemType);

    public static SandboxValue FromMap(
        IReadOnlyDictionary<SandboxValue, SandboxValue> values,
        SandboxType keyType,
        SandboxType valueType)
        => new MapValue(values, keyType, valueType);

    public static SandboxValue FromRecord(IReadOnlyList<SandboxValue> fields) => new RecordValue(fields);

    internal static SandboxValue FromOwnedRecord(SandboxValue[] fields) => RecordValue.FromOwnedFields(fields);
}

public sealed record UnitValue : SandboxValue
{
    public override SandboxType Type => SandboxType.Unit;
}

public sealed record BoolValue(bool Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.Bool;
}

public sealed record I32Value(int Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.I32;
}

public sealed record I64Value(long Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.I64;
}

public sealed record F64Value(double Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.F64;
}

public sealed record StringValue(string Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.String;
}

public sealed record OpaqueIdValue(string TypeName, string Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.Scalar(TypeName);
}

public sealed record SandboxPath(string RelativePath)
{
    public override string ToString() => RelativePath;
}

public sealed record SandboxPathValue(SandboxPath Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.SandboxPath;
}

public sealed record SandboxUri(string Value)
{
    public override string ToString() => Value;
}

public sealed record SandboxUriValue(SandboxUri Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.Scalar("SandboxUri");
}

public sealed record ListValue(IReadOnlyList<SandboxValue> Values, SandboxType ItemType) : SandboxValue
{
    private IReadOnlyList<SandboxValue> _values = Snapshot(Values);

    public IReadOnlyList<SandboxValue> Values { get => _values; init => _values = Snapshot(value); }

    /// <summary>
    /// Constructs a list value over an array the caller has just allocated, fully
    /// populated, and will not expose for mutation, avoiding a second defensive copy.
    /// Internal because the owned-array contract cannot be enforced for external callers.
    /// </summary>
    internal static ListValue FromOwnedValues(SandboxValue[] values, SandboxType itemType)
        => new(new OwnedSnapshot(ModelCopy.WrapOwned(values)), itemType);

    private static IReadOnlyList<SandboxValue> Snapshot(IReadOnlyList<SandboxValue> values)
        => values is OwnedSnapshot owned ? owned.Values : ModelCopy.List(values);

    private sealed class OwnedSnapshot(IReadOnlyList<SandboxValue> values) : IReadOnlyList<SandboxValue>
    {
        public IReadOnlyList<SandboxValue> Values { get; } = values;

        public SandboxValue this[int index] => Values[index];

        public int Count => Values.Count;

        public IEnumerator<SandboxValue> GetEnumerator() => Values.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public override SandboxType Type => SandboxType.List(ItemType);

    public bool Equals(ListValue? other)
    {
        if (other is null ||
            !ItemType.Equals(other.ItemType) ||
            Values.Count != other.Values.Count)
        {
            return false;
        }

        for (var i = 0; i < Values.Count; i++)
        {
            if (!Values[i].Equals(other.Values[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ItemType);
        foreach (var value in Values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}

public sealed record RecordValue : SandboxValue
{
    private IReadOnlyList<SandboxValue> _fields;

    public RecordValue(IReadOnlyList<SandboxValue> Fields)
        => _fields = Snapshot(Fields);

    public IReadOnlyList<SandboxValue> Fields { get => _fields; init => _fields = Snapshot(value); }

    /// <summary>
    /// Constructs a record value over an array the caller has just allocated, fully populated, and will
    /// not retain or mutate, avoiding the defensive copy <see cref="SandboxValue.FromRecord"/> performs.
    /// Internal because the owned-array contract cannot be enforced for external callers.
    /// </summary>
    internal static RecordValue FromOwnedFields(SandboxValue[] fields)
        => new(new OwnedSnapshot(ModelCopy.WrapOwned(fields)));

    private static IReadOnlyList<SandboxValue> Snapshot(IReadOnlyList<SandboxValue> fields)
        => fields is OwnedSnapshot owned ? owned.Fields : ModelCopy.List(fields);

    private sealed class OwnedSnapshot(IReadOnlyList<SandboxValue> fields) : IReadOnlyList<SandboxValue>
    {
        public IReadOnlyList<SandboxValue> Fields { get; } = fields;

        public SandboxValue this[int index] => Fields[index];

        public int Count => Fields.Count;

        public IEnumerator<SandboxValue> GetEnumerator() => Fields.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public override SandboxType Type
    {
        get
        {
            var fieldTypes = new SandboxType[_fields.Count];
            for (var i = 0; i < _fields.Count; i++)
            {
                fieldTypes[i] = _fields[i].Type;
            }

            return SandboxType.Record(fieldTypes);
        }
    }

    public bool Equals(RecordValue? other)
    {
        if (other is null || Fields.Count != other.Fields.Count)
        {
            return false;
        }

        for (var i = 0; i < Fields.Count; i++)
        {
            if (!Fields[i].Equals(other.Fields[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SandboxType.RecordName, StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            hash.Add(field);
        }

        return hash.ToHashCode();
    }
}

public sealed record MapValue(
    IReadOnlyDictionary<SandboxValue, SandboxValue> Values,
    SandboxType KeyType,
    SandboxType ValueType) : SandboxValue
{
    private IReadOnlyDictionary<SandboxValue, SandboxValue> _values = ModelCopy.ValueDictionary(Values);

    public IReadOnlyDictionary<SandboxValue, SandboxValue> Values { get => _values; init => _values = ModelCopy.ValueDictionary(value); }

    public override SandboxType Type => SandboxType.Map(KeyType, ValueType);

    public bool Equals(MapValue? other)
    {
        if (other is null ||
            !KeyType.Equals(other.KeyType) ||
            !ValueType.Equals(other.ValueType) ||
            Values.Count != other.Values.Count)
        {
            return false;
        }

        foreach (var entry in Values)
        {
            if (!other.Values.TryGetValue(entry.Key, out var otherValue) ||
                !entry.Value.Equals(otherValue))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        // Maps have no defined enumeration order, so combine entry hashes
        // commutatively (XOR) to keep the hash order-independent.
        var keyTypeHash = KeyType.GetHashCode();
        var valueTypeHash = ValueType.GetHashCode();
        var entriesHash = 0;
        foreach (var entry in Values)
        {
            entriesHash ^= HashCode.Combine(entry.Key, entry.Value);
        }

        return HashCode.Combine(keyTypeHash, valueTypeHash, entriesHash);
    }
}
