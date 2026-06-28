namespace DotBoxD.Plugins.Runtime;

internal readonly struct CachedPipelineFanout
{
    private static readonly object[] EmptyPipelines = Array.Empty<object>();
    private readonly object[]? _pipelines;

    private CachedPipelineFanout(object[] pipelines)
        => _pipelines = pipelines;

    public int Count => Pipelines.Length;

    public object this[int index] => Pipelines[index];

    public static CachedPipelineFanout Empty => default;

    public static CachedPipelineFanout From(List<object>? pipelines)
        => pipelines is null || pipelines.Count == 0
            ? Empty
            : new CachedPipelineFanout(pipelines.ToArray());

    public Enumerator GetEnumerator() => new(Pipelines);

    private object[] Pipelines => _pipelines ?? EmptyPipelines;

    internal struct Enumerator
    {
        private readonly object[] _pipelines;
        private int _index;

        public Enumerator(object[] pipelines)
        {
            _pipelines = pipelines;
            _index = -1;
        }

        public object Current => _pipelines[_index];

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next >= _pipelines.Length)
            {
                return false;
            }

            _index = next;
            return true;
        }
    }
}
