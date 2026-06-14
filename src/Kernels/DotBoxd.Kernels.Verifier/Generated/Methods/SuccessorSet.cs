namespace DotBoxd.Kernels.Verifier;

/// <summary>
/// Compact, allocation-free container for the control-flow successors of a single
/// generated IL instruction. The overwhelmingly common cases (zero, one, or two
/// successors) are stored inline, so no per-instruction array is allocated for the
/// linear fallthrough and simple-branch shapes. Only multi-target <c>switch</c>
/// instructions (three or more successors) fall back to a heap array.
/// </summary>
internal readonly struct SuccessorSet
{
    private readonly int _first;
    private readonly int _second;
    private readonly int[]? _overflow;
    private readonly int _count;

    private SuccessorSet(int first, int second, int[]? overflow, int count)
    {
        _first = first;
        _second = second;
        _overflow = overflow;
        _count = count;
    }

    public static SuccessorSet Empty => default;

    public int Count => _count;

    public int this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (_overflow is not null)
            {
                return _overflow[index];
            }

            return index == 0 ? _first : _second;
        }
    }

    public static SuccessorSet One(int successor) => new(successor, default, overflow: null, count: 1);

    public static SuccessorSet Two(int first, int second) => new(first, second, overflow: null, count: 2);

    /// <summary>
    /// Builds a successor set from an ordered sequence, preserving iteration order
    /// exactly. Inline storage is used for up to two successors; larger sets (switch
    /// instructions) keep the materialized array.
    /// </summary>
    public static SuccessorSet From(IReadOnlyList<int> successors)
    {
        return successors.Count switch
        {
            0 => Empty,
            1 => One(successors[0]),
            2 => Two(successors[0], successors[1]),
            _ => new SuccessorSet(default, default, AsArray(successors), successors.Count)
        };
    }

    private static int[] AsArray(IReadOnlyList<int> successors)
        => successors as int[] ?? successors.ToArray();

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly SuccessorSet _set;
        private int _index;

        internal Enumerator(SuccessorSet set)
        {
            _set = set;
            _index = -1;
        }

        public int Current => _set[_index];

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next >= _set._count)
            {
                return false;
            }

            _index = next;
            return true;
        }
    }
}
