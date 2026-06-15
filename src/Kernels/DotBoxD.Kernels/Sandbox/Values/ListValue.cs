namespace DotBoxD.Kernels;

using System.Collections.Immutable;

public sealed record ListValue(IReadOnlyList<SandboxValue> Values, SandboxType ItemType) : SandboxValue, IReadOnlyList<SandboxValue>
{
    private IReadOnlyList<SandboxValue> _values = Snapshot(Values);
    private readonly bool _ownsValues;

    public IReadOnlyList<SandboxValue> Values { get => this; init => _values = Snapshot(value); }

    /// <summary>
    /// Constructs a list value over an array the caller has just allocated, fully
    /// populated, and will not expose for mutation, avoiding a second defensive copy.
    /// Internal because the owned-array contract cannot be enforced for external callers.
    /// </summary>
    internal static ListValue FromOwnedValues(SandboxValue[] values, SandboxType itemType)
        => new(values, itemType, ownsValues: true);

    /// <summary>
    /// Rebinds the backing array in place to reuse this instance's allocation across dispatches.
    /// Only valid on a list built via <see cref="FromOwnedValues"/>: such a list is private to the
    /// builder that owns its buffer and is never handed out for sharing. Resetting a normally
    /// constructed (defensively copied, potentially externally visible) list would let a retained
    /// reference observe a later dispatch's input, so that is rejected.
    /// </summary>
    internal void ResetOwnedValues(SandboxValue[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!_ownsValues)
        {
            throw new InvalidOperationException(
                "ResetOwnedValues is only valid on a list constructed via FromOwnedValues.");
        }

        _values = values;
    }

    private ListValue(SandboxValue[] values, SandboxType itemType, bool ownsValues)
        : this(Array.Empty<SandboxValue>(), itemType)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = ownsValues ? values : values.ToArray();
        _ownsValues = ownsValues;
    }

    private static IReadOnlyList<SandboxValue> Snapshot(IReadOnlyList<SandboxValue> values)
        => values switch
        {
            null => throw new ArgumentNullException(nameof(values)),
            ListValue list => list._values,
            ImmutableList<SandboxValue> immutable => immutable,
            _ => ModelCopy.List(values)
        };

    /// <summary>
    /// Returns a new list with <paramref name="item"/> appended, sharing structure with this list via an
    /// immutable backing so the append is O(log n) rather than an O(n) copy. Charged fuel/allocation are
    /// unchanged by the caller; only the runtime data structure and wall-time differ.
    /// </summary>
    internal ListValue Append(SandboxValue item)
    {
        var immutable = _values as ImmutableList<SandboxValue> ?? ImmutableList.CreateRange(_values);
        return new ListValue(immutable.Add(item), ItemType);
    }

    public SandboxValue this[int index] => _values[index];

    public int Count => _values.Count;

    public IEnumerator<SandboxValue> GetEnumerator()
        => ((IEnumerable<SandboxValue>)_values).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"ListValue {{ Count = {_values.Count}, ItemType = {ItemType} }}";

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
