using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Sandbox;

public abstract record SandboxValue
{
    private const int CachedI32Min = -1;
    private const int CachedI32Max = 256;

    private static readonly SandboxValue True = new BoolValue(true);
    private static readonly SandboxValue False = new BoolValue(false);
    private static readonly SandboxValue[] CachedI32Values = CreateCachedI32Values();

    public static SandboxValue Unit { get; } = new UnitValue();

    public abstract SandboxType Type { get; }

    public static SandboxValue FromBool(bool value) => value ? True : False;

    public static SandboxValue FromInt32(int value)
        => value >= CachedI32Min && value <= CachedI32Max
            ? CachedI32Values[value - CachedI32Min]
            : new I32Value(value);

    public static SandboxValue FromInt64(long value) => new I64Value(value);

    public static SandboxValue FromDouble(double value)
        => double.IsFinite(value)
            ? new F64Value(value)
            : throw new ArgumentOutOfRangeException(nameof(value), value, "F64 values must be finite.");

    public static SandboxValue FromString(string value) => new StringValue(value);

    public static SandboxValue FromGuid(System.Guid value) => new GuidValue(value);

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

    internal static SandboxValue FromOwnedMap(
        MapValueBuilder values,
        SandboxType keyType,
        SandboxType valueType)
        => MapValue.FromOwnedValues(values, keyType, valueType);

    public static SandboxValue FromRecord(IReadOnlyList<SandboxValue> fields) => new RecordValue(fields);

    internal static SandboxValue FromOwnedRecord(SandboxValue[] fields) => RecordValue.FromOwnedFields(fields);

    private static SandboxValue[] CreateCachedI32Values()
    {
        var values = new SandboxValue[CachedI32Max - CachedI32Min + 1];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = new I32Value(CachedI32Min + i);
        }

        return values;
    }
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

/// <summary>
/// A 128-bit <see cref="System.Guid"/> carried as a first-class sandbox scalar. The kernel never operates on
/// it (no arithmetic, comparison, or string semantics) — it only reads, stores, projects, and returns it — so
/// it crosses the wire as a compact 16-byte value rather than its 36-char text form.
/// </summary>
public sealed record GuidValue(System.Guid Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.Guid;
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
    public override SandboxType Type => SandboxType.SandboxUri;
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
        => new(new OwnedSnapshot(fields));

    private static IReadOnlyList<SandboxValue> Snapshot(IReadOnlyList<SandboxValue> fields)
        => fields is OwnedSnapshot owned ? owned : ModelCopy.List(fields);

    private sealed class OwnedSnapshot(SandboxValue[] fields) : IReadOnlyList<SandboxValue>
    {
        public SandboxValue this[int index] => fields[index];

        public int Count => fields.Length;

        public IEnumerator<SandboxValue> GetEnumerator()
            => ((IEnumerable<SandboxValue>)fields).GetEnumerator();

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

// MapValue lives in MapValue.cs (kept out of this file to stay under the line cap).
