namespace SafeIR;

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
            : throw new ArgumentException("Opaque IDs must use a known ID type and a safe ID value.", nameof(value));

    public static SandboxValue FromPlayerId(string value) => FromOpaqueId("PlayerId", value);

    public static SandboxValue FromItemId(string value) => FromOpaqueId("ItemId", value);

    public static SandboxValue FromQuestId(string value) => FromOpaqueId("QuestId", value);

    public static SandboxValue FromMapId(string value) => FromOpaqueId("MapId", value);

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
        var snapshot = ModelCopy.List(values);
        return new ListValue(snapshot, snapshot.Count == 0 ? SandboxType.Unit : snapshot[0].Type);
    }

    public static SandboxValue FromList(IReadOnlyList<SandboxValue> values, SandboxType itemType)
        => new ListValue(values, itemType);

    public static SandboxValue FromMap(
        IReadOnlyDictionary<SandboxValue, SandboxValue> values,
        SandboxType keyType,
        SandboxType valueType)
        => new MapValue(values, keyType, valueType);
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
    private IReadOnlyList<SandboxValue> _values = ModelCopy.List(Values);

    public IReadOnlyList<SandboxValue> Values { get => _values; init => _values = ModelCopy.List(value); }

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
