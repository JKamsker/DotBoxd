using System.Collections;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal readonly struct EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
{
    private readonly T[]? _items;

    public EquatableArray(IEnumerable<T> items)
        => _items = items is null ? throw new ArgumentNullException(nameof(items)) : items.ToArray();

    private EquatableArray(T[] items)
        => _items = items;

    public static EquatableArray<T> FromOwned(T[] items)
        => items.Length == 0 ? default : new EquatableArray<T>(items);

    private T[] Items => _items ?? Array.Empty<T>();

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (Count != other.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < Count; i++)
        {
            if (!comparer.Equals(this[i], other[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
        => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in Items)
            {
                hash = (hash * 31) + (item is null ? 0 : item.GetHashCode());
            }

            return hash;
        }
    }

    public Enumerator GetEnumerator()
        => new(Items);

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
        => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)Items).GetEnumerator();

    public struct Enumerator
    {
        private readonly T[] _items;
        private int _index;

        internal Enumerator(T[] items)
        {
            _items = items;
            _index = -1;
        }

        public T Current => _items[_index];

        public bool MoveNext()
        {
            var next = _index + 1;
            if ((uint)next >= (uint)_items.Length)
            {
                return false;
            }

            _index = next;
            return true;
        }
    }
}
