namespace SafeIR.PluginAnalyzer;

using System.Collections;

internal readonly struct EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
{
    private readonly T[]? _items;

    public EquatableArray(IEnumerable<T> items)
        => _items = items.ToArray();

    private T[] Items => _items ?? Array.Empty<T>();

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (Count != other.Count) {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < Count; i++) {
            if (!comparer.Equals(this[i], other[i])) {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
        => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked {
            var hash = 17;
            foreach (var item in Items) {
                hash = (hash * 31) + (item is null ? 0 : item.GetHashCode());
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
        => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}
