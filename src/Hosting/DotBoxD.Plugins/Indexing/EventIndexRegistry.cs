using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins.Indexing;

/// <summary>
/// A first-class, reusable host dispatch index (issue #50). A host registers a subscription kernel together
/// with its generated <see cref="IndexedPredicate"/>s; the registry recomputes them from verified IR and
/// compiles them into an
/// <see cref="EventIndexMatcher{TEvent}"/> (precompiled getters, no per-event reflection) and, when the host
/// publishes an event, runs the cheap index check <i>before</i> entering the sandbox. Events the index
/// rejects never reach the verified IR; survivors are dispatched to <see cref="InstalledKernel"/> as the
/// correctness authority — the verified <c>ShouldHandle</c> still runs after a matching index check because
/// package-supplied coverage metadata is not trusted across the manifest boundary.
/// <para>
/// This is the "register a subscription and get index-based prefiltering without writing your own matcher"
/// surface. Subscriptions whose predicates touch no indexed field are rejected by <see cref="Register"/>
/// (returns <c>false</c>) so the host can leave them on its broad pipeline.
/// </para>
/// </summary>
public sealed partial class EventIndexRegistry
{
    private readonly object _gate = new();
    private readonly Action<SubscriptionDeliveryFault>? _onFault;
    private readonly Dictionary<Type, IEventIndexChannel> _channels = [];
    private readonly List<Task> _inFlight = [];
    private long _considered;
    private long _prefiltered;
    private long _dispatched;

    public EventIndexRegistry(Action<SubscriptionDeliveryFault>? onFault = null)
        => _onFault = onFault;

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

        _ = predicates;
        var trustedPredicates = TrustedIndexPredicateExtractor.Extract(kernel.Package, adapter.Parameters);
        var matcher = EventIndexMatcher<TEvent>.Create(trustedPredicates);
        if (!matcher.HasIndex)
        {
            return false;
        }

        _ = indexCoversPredicate;
        // Index metadata travels in the mutable package manifest, so neither predicates nor coverage claims
        // are trusted. The runtime recomputes necessary predicates from verified ShouldHandle IR and always
        // runs the verified predicate after an index survivor.
        const bool fullyCovered = false;
        lock (_gate)
        {
            if (!_channels.TryGetValue(typeof(TEvent), out var existing))
            {
                existing = new EventIndexChannel<TEvent>(adapter);
                _channels[typeof(TEvent)] = existing;
            }

            ((EventIndexChannel<TEvent>)existing).Add(new EventIndexEntry<TEvent>(matcher, kernel, fullyCovered));
        }

        kernel.RegisterRevocationCallback(Unregister);
        return true;
    }

    /// <summary>
    /// Runs every registered <typeparamref name="TEvent"/> subscription's cheap index check against
    /// <paramref name="value"/>; survivors are dispatched to the verified IR fire-and-forget (the host's
    /// broad pipeline keeps non-indexed subscriptions). Prefilter counters update synchronously.
    /// </summary>
    public void Publish<TEvent>(TEvent value, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

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
            Track(Task.Run(() => DispatchAsync(channel.Adapter, entry, value, cancellationToken, _onFault)));
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
        CancellationToken cancellationToken,
        Action<SubscriptionDeliveryFault>? onFault)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        bool shouldHandle;
        try
        {
            // The verified IR predicate remains the authority; manifest coverage claims are not trusted.
            shouldHandle = entry.FullyCovered ||
                await entry.Kernel.ShouldHandleAsync(adapter, value, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (SandboxRuntimeException ex) when (WasCallerCancelled(ex, cancellationToken))
        {
            return;
        }
        catch (Exception ex)
        {
            Report<TEvent>(onFault, ex, SubscriptionDeliveryStage.Filter);
            return;
        }

        if (!shouldHandle || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await entry.Kernel.HandleAsync(adapter, value, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SandboxRuntimeException ex) when (WasCallerCancelled(ex, cancellationToken))
        {
        }
        catch (Exception ex)
        {
            Report<TEvent>(onFault, ex, SubscriptionDeliveryStage.Handler);
        }
    }

    private static bool WasCallerCancelled(SandboxRuntimeException exception, CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested && exception.Error.Code == SandboxErrorCode.Cancelled;

    private static void Report<TEvent>(
        Action<SubscriptionDeliveryFault>? onFault,
        Exception exception,
        SubscriptionDeliveryStage stage)
    {
        if (onFault is null)
        {
            return;
        }

        try
        {
            onFault(new SubscriptionDeliveryFault(typeof(TEvent), stage, exception));
        }
        catch
        {
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
