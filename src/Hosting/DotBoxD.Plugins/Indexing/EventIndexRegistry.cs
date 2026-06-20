using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Indexing;

/// <summary>
/// A first-class, reusable host dispatch index (issue #50). A host registers a subscription kernel together
/// with its manifest <see cref="IndexedPredicate"/>s; the registry compiles them into an
/// <see cref="EventIndexMatcher{TEvent}"/> (precompiled getters, no per-event reflection) and, when the host
/// publishes an event, runs the cheap index check <i>before</i> entering the sandbox. Events the index
/// rejects never reach the verified IR; survivors are dispatched to <see cref="InstalledKernel"/> as the
/// correctness authority — the verified <c>ShouldHandle</c> still runs unless the index fully covers the
/// predicate (<see cref="IndexedPredicate"/> set == the whole predicate and every path is an index key).
/// <para>
/// This is the "register a subscription and get index-based prefiltering without writing your own matcher"
/// surface. Subscriptions whose predicates touch no indexed field are rejected by <see cref="Register"/>
/// (returns <c>false</c>) so the host can leave them on its broad pipeline.
/// </para>
/// </summary>
public sealed class EventIndexRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, IEventIndexChannel> _channels = [];
    private readonly List<Task> _inFlight = [];
    private long _considered;
    private long _prefiltered;
    private long _dispatched;

    /// <summary>Aggregate prefilter diagnostics across every published event and registered subscription.</summary>
    public EventIndexStats Stats => new(
        Interlocked.Read(ref _considered),
        Interlocked.Read(ref _prefiltered),
        Interlocked.Read(ref _dispatched));

    /// <summary>
    /// Registers <paramref name="kernel"/> as an indexed subscription for <typeparamref name="TEvent"/>.
    /// Returns <c>false</c> (registering nothing) when none of <paramref name="predicates"/> map onto an
    /// <see cref="EventIndexKeyAttribute"/> field — the caller should keep such a subscription on its broad
    /// pipeline. Returns <c>true</c> when the subscription is now served from the index.
    /// </summary>
    public bool Register<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        InstalledKernel kernel,
        IReadOnlyList<IndexedPredicate> predicates,
        bool indexCoversPredicate)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(predicates);

        var matcher = EventIndexMatcher<TEvent>.Create(predicates);
        if (!matcher.HasIndex)
        {
            return false;
        }

        // The host may skip the verified IR only when the index serves the *entire* predicate: the manifest
        // reported full coverage AND every predicate path is one this host indexes (so nothing was dropped or
        // left to the IR). Double-typed predicates are excluded from the skip — the verified IR rejects
        // non-finite doubles (NaN/Infinity) at marshalling, which the cheap index does not replicate, so a
        // double index key stays prefilter-only and the IR remains the authority for those events.
        var fullyCovered = indexCoversPredicate &&
            matcher.HonoredPredicates.Count == predicates.Count &&
            !AnyDoubleTyped(matcher.HonoredPredicates);
        lock (_gate)
        {
            if (!_channels.TryGetValue(typeof(TEvent), out var existing))
            {
                existing = new EventIndexChannel<TEvent>(adapter);
                _channels[typeof(TEvent)] = existing;
            }

            ((EventIndexChannel<TEvent>)existing).Add(new EventIndexEntry<TEvent>(matcher, kernel, fullyCovered));
        }

        return true;
    }

    /// <summary>
    /// Runs every registered <typeparamref name="TEvent"/> subscription's cheap index check against
    /// <paramref name="value"/>; survivors are dispatched to the verified IR fire-and-forget (the host's
    /// broad pipeline keeps non-indexed subscriptions). Prefilter counters update synchronously.
    /// </summary>
    public void Publish<TEvent>(TEvent value, CancellationToken cancellationToken = default)
    {
        EventIndexChannel<TEvent>? channel;
        lock (_gate)
        {
            channel = _channels.TryGetValue(typeof(TEvent), out var existing)
                ? (EventIndexChannel<TEvent>)existing
                : null;
        }

        if (channel is null)
        {
            return;
        }

        foreach (var entry in channel.Snapshot())
        {
            Interlocked.Increment(ref _considered);

            bool couldMatch;
            try
            {
                couldMatch = entry.Matcher.CouldMatch(value);
            }
            catch
            {
                // A matcher must never crash the host's publish loop. If a check cannot be evaluated at all,
                // fail open: let the verified IR (the authority) decide rather than dropping the event.
                couldMatch = true;
            }

            if (!couldMatch)
            {
                Interlocked.Increment(ref _prefiltered);
                continue;
            }

            Interlocked.Increment(ref _dispatched);
            Track(Task.Run(() => DispatchAsync(channel.Adapter, entry, value, cancellationToken)));
        }
    }

    /// <summary>Removes every registration owned by <paramref name="kernel"/> (e.g. when a host uninstalls it).</summary>
    public void Unregister(InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        lock (_gate)
        {
            foreach (var channel in _channels.Values)
            {
                channel.Remove(kernel);
            }
        }
    }

    private static bool AnyDoubleTyped(IReadOnlyList<IndexedPredicate> predicates)
    {
        for (var i = 0; i < predicates.Count; i++)
        {
            if (string.Equals(predicates[i].ValueType, "double", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Awaits every in-flight dispatch launched by <see cref="Publish"/> (e.g. on host shutdown).</summary>
    public async Task DrainAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_gate)
            {
                pending = [.. _inFlight];
            }

            if (pending.Length == 0)
            {
                return;
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    private static async Task DispatchAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        EventIndexEntry<TEvent> entry,
        TEvent value,
        CancellationToken cancellationToken)
    {
        try
        {
            // Full coverage: the index already proved the predicate, so the verified ShouldHandle is
            // redundant and may be skipped. Otherwise the verified IR predicate remains the authority.
            if (entry.FullyCovered || await entry.Kernel.ShouldHandleAsync(adapter, value, cancellationToken).ConfigureAwait(false))
            {
                await entry.Kernel.HandleAsync(adapter, value, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Fire-and-forget dispatch mirrors the broad subscription pipeline: a faulting kernel cannot
            // take down the host's publish loop.
        }
    }

    private void Track(Task task)
    {
        lock (_gate)
        {
            _inFlight.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_gate)
                {
                    _inFlight.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

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

/// <summary>
/// Prefilter diagnostics for an <see cref="EventIndexRegistry"/>: how many indexed checks ran
/// (<see cref="Considered"/>), how many events the index rejected before any sandbox entry
/// (<see cref="Prefiltered"/>), and how many survived to the verified IR (<see cref="Dispatched"/>).
/// <c>Considered == Prefiltered + Dispatched</c> holds once publishing is quiescent; a snapshot taken
/// while <see cref="EventIndexRegistry.Publish"/> is running concurrently may observe an in-flight
/// increment between the three reads.
/// </summary>
public readonly record struct EventIndexStats(long Considered, long Prefiltered, long Dispatched);
