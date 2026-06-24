using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Indexing;

public sealed partial class EventIndexRegistry
{
    private interface IEventIndexChannel
    {
        void Remove(InstalledKernel kernel);
    }

    private sealed class EventIndexChannel<TEvent>(IPluginEventAdapter<TEvent> adapter) : IEventIndexChannel
    {
        private readonly object _gate = new();

        // Copy-on-write under _gate; volatile so Publish reads the latest snapshot without locking.
        private volatile EventIndexEntry<TEvent>[] _entries = [];

        public IPluginEventAdapter<TEvent> Adapter { get; } = adapter;

        public void Add(EventIndexEntry<TEvent> entry)
        {
            lock (_gate)
            {
                _entries = [.. _entries, entry];
            }
        }

        public void Remove(InstalledKernel kernel)
        {
            lock (_gate)
            {
                _entries = [.. _entries.Where(entry => !ReferenceEquals(entry.Kernel, kernel))];
            }
        }

        public EventIndexEntry<TEvent>[] Snapshot() => _entries;
    }

    private sealed record EventIndexEntry<TEvent>(
        EventIndexMatcher<TEvent> Matcher,
        InstalledKernel Kernel,
        bool FullyCovered);
}
