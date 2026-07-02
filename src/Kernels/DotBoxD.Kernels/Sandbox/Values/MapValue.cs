using System.Collections.Immutable;
namespace DotBoxD.Kernels.Sandbox.Values;

public sealed record MapValue(
    IReadOnlyDictionary<SandboxValue, SandboxValue> Values,
    SandboxType KeyType,
    SandboxType ValueType) : SandboxValue
{
    private IReadOnlyDictionary<SandboxValue, SandboxValue> _values = Snapshot(Values);

    public IReadOnlyDictionary<SandboxValue, SandboxValue> Values { get => _values; init => _values = Snapshot(value); }

    internal EntryEnumerable Entries
        => _values is DictionarySnapshot snapshot
            ? new EntryEnumerable(snapshot.Dictionary, null)
            : new EntryEnumerable(null, (ImmutableDictionary<SandboxValue, SandboxValue>)_values);

    /// <summary>
    /// Constructs a map value over a dictionary the caller has just allocated,
    /// fully populated, and will not retain or mutate.
    /// </summary>
    internal static MapValue FromOwnedValues(
        MapValueBuilder values,
        SandboxType keyType,
        SandboxType valueType)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new MapValue(new DictionarySnapshot(values.Consume()), keyType, valueType);
    }

    private static IReadOnlyDictionary<SandboxValue, SandboxValue> Snapshot(IReadOnlyDictionary<SandboxValue, SandboxValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values is DictionarySnapshot snapshot)
        {
            return snapshot;
        }

        if (values is ImmutableDictionary<SandboxValue, SandboxValue> immutable)
        {
            return immutable;
        }

        var dictionary = new Dictionary<SandboxValue, SandboxValue>(values);
        return new DictionarySnapshot(dictionary);
    }

    /// <summary>
    /// Returns a new map with <paramref name="key"/> set to <paramref name="value"/>, sharing structure with
    /// this map via an immutable backing so the update is O(log n) rather than an O(n) dictionary copy.
    /// </summary>
    internal MapValue SetEntry(SandboxValue key, SandboxValue value)
    {
        var immutable = _values as ImmutableDictionary<SandboxValue, SandboxValue>
            ?? ImmutableDictionary.CreateRange(((DictionarySnapshot)_values).Dictionary);
        return new MapValue(immutable.SetItem(key, value), KeyType, ValueType);
    }

    /// <summary>
    /// Returns a new map with <paramref name="key"/> removed, sharing structure with this map via an
    /// immutable backing so removal avoids copying the whole dictionary on every operation.
    /// </summary>
    internal MapValue RemoveEntry(SandboxValue key)
    {
        var immutable = _values as ImmutableDictionary<SandboxValue, SandboxValue>
            ?? ImmutableDictionary.CreateRange(((DictionarySnapshot)_values).Dictionary);
        return new MapValue(immutable.Remove(key), KeyType, ValueType);
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
        foreach (var entry in Entries)
        {
            entriesHash ^= HashCode.Combine(entry.Key, entry.Value);
        }

        return HashCode.Combine(keyTypeHash, valueTypeHash, entriesHash);
    }

    private sealed class DictionarySnapshot(
        Dictionary<SandboxValue, SandboxValue> dictionary) : IReadOnlyDictionary<SandboxValue, SandboxValue>
    {
        internal Dictionary<SandboxValue, SandboxValue> Dictionary => dictionary;

        public IEnumerable<SandboxValue> Keys => dictionary.Keys;

        public IEnumerable<SandboxValue> Values => dictionary.Values;

        public int Count => dictionary.Count;

        public SandboxValue this[SandboxValue key] => dictionary[key];

        public bool ContainsKey(SandboxValue key)
            => dictionary.ContainsKey(key);

        public bool TryGetValue(SandboxValue key, out SandboxValue value)
            => dictionary.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<SandboxValue, SandboxValue>> GetEnumerator()
            => dictionary.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    internal readonly struct EntryEnumerable(
        Dictionary<SandboxValue, SandboxValue>? dictionary,
        ImmutableDictionary<SandboxValue, SandboxValue>? immutable)
    {
        public Enumerator GetEnumerator() => new(dictionary, immutable);
    }

    internal struct Enumerator
    {
        private readonly bool _isDictionary;
        private Dictionary<SandboxValue, SandboxValue>.Enumerator _dictionary;
        private ImmutableDictionary<SandboxValue, SandboxValue>.Enumerator _immutable;

        public Enumerator(
            Dictionary<SandboxValue, SandboxValue>? dictionary,
            ImmutableDictionary<SandboxValue, SandboxValue>? immutable)
        {
            if (dictionary is not null)
            {
                _isDictionary = true;
                _dictionary = dictionary.GetEnumerator();
                _immutable = default;
                return;
            }

            _isDictionary = false;
            _dictionary = default;
            _immutable = (immutable ?? ImmutableDictionary<SandboxValue, SandboxValue>.Empty).GetEnumerator();
        }

        public KeyValuePair<SandboxValue, SandboxValue> Current
            => _isDictionary ? _dictionary.Current : _immutable.Current;

        public bool MoveNext()
            => _isDictionary ? _dictionary.MoveNext() : _immutable.MoveNext();
    }
}
