namespace DotBoxD.Kernels;

using System.Collections.Immutable;

public sealed record MapValue(
    IReadOnlyDictionary<SandboxValue, SandboxValue> Values,
    SandboxType KeyType,
    SandboxType ValueType) : SandboxValue
{
    private IReadOnlyDictionary<SandboxValue, SandboxValue> _values = Snapshot(Values);

    public IReadOnlyDictionary<SandboxValue, SandboxValue> Values { get => _values; init => _values = Snapshot(value); }

    private static IReadOnlyDictionary<SandboxValue, SandboxValue> Snapshot(IReadOnlyDictionary<SandboxValue, SandboxValue> values)
        // An ImmutableDictionary is already an immutable, structurally-shared snapshot; store it directly so
        // map.set can share structure and run in O(log n) instead of copying the whole dictionary.
        => values is ImmutableDictionary<SandboxValue, SandboxValue> immutable
            ? immutable
            : ModelCopy.ValueDictionary(values);

    /// <summary>
    /// Returns a new map with <paramref name="key"/> set to <paramref name="value"/>, sharing structure with
    /// this map via an immutable backing so the update is O(log n) rather than an O(n) dictionary copy.
    /// </summary>
    internal MapValue SetEntry(SandboxValue key, SandboxValue value)
    {
        var immutable = _values as ImmutableDictionary<SandboxValue, SandboxValue>
            ?? ImmutableDictionary.CreateRange(_values);
        return new MapValue(immutable.SetItem(key, value), KeyType, ValueType);
    }

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
