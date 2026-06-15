namespace DotBoxD.Kernels.Compiler.Emitters;

using DotBoxD.Kernels.Compiler;

internal sealed class ReflectionEmitMemoryCache
{
    private const int Capacity = 64;

    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recency = new();
    private readonly object _gate = new();

    public bool TryGet(string key, out CompiledArtifact artifact)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recency.Remove(node);
                _recency.AddLast(node);
                artifact = node.Value.Artifact;
                return true;
            }
        }

        artifact = null!;
        return false;
    }

    public void Add(string key, CompiledArtifact artifact)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _recency.Remove(existing);
                _entries.Remove(key);
            }

            var node = _recency.AddLast(new Entry(key, artifact));
            _entries[key] = node;
            if (_entries.Count <= Capacity)
            {
                return;
            }

            var oldest = _recency.First;
            if (oldest is not null)
            {
                _recency.Remove(oldest);
                _entries.Remove(oldest.Value.Key);
            }
        }
    }

    private readonly record struct Entry(string Key, CompiledArtifact Artifact);
}
