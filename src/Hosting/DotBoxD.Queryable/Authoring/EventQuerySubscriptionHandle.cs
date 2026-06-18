using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Planning;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// The lifetime and observability handle for a registered event query. Disposing it removes the
/// subscription (and its index entries) from the dispatcher. The counters expose how indexed dispatch
/// behaved: how many events the dispatcher saw versus how many actually reached this subscription's filter
/// and handler — evidence that the index prefiltered instead of fanning every event out to every subscriber.
/// </summary>
public sealed class EventQuerySubscriptionHandle : IDisposable
{
    private readonly Func<long> _eventsObserved;
    private readonly Func<bool> _isCompiled;
    private Action? _unsubscribe;
    private long _filterEvaluations;
    private long _matches;
    private long _dispatches;

    internal EventQuerySubscriptionHandle(
        EventQueryDocument document,
        EventQueryPlan plan,
        string fingerprint,
        Func<long> eventsObserved,
        Func<bool> isCompiled,
        Action unsubscribe)
    {
        Document = document;
        Plan = plan;
        Fingerprint = fingerprint;
        _eventsObserved = eventsObserved;
        _isCompiled = isCompiled;
        _unsubscribe = unsubscribe;
    }

    /// <summary>The portable query document this subscription registered.</summary>
    public EventQueryDocument Document { get; }

    /// <summary>The host plan derived from the document (index predicates, residual, coverage).</summary>
    public EventQueryPlan Plan { get; }

    /// <summary>The canonical query fingerprint.</summary>
    public string Fingerprint { get; }

    /// <summary>Total events of this type the dispatcher observed across all of its subscriptions.</summary>
    public long EventsObserved => _eventsObserved();

    /// <summary>How many observed events reached this subscription's filter (its indexed candidate count).</summary>
    public long FilterEvaluations => Interlocked.Read(ref _filterEvaluations);

    /// <summary>How many events passed this subscription's filter.</summary>
    public long Matches => Interlocked.Read(ref _matches);

    /// <summary>How many projected payloads were dispatched to the handler.</summary>
    public long Dispatches => Interlocked.Read(ref _dispatches);

    /// <summary>Whether this subscription's filter has been promoted to the compiled (hot-path) tier.</summary>
    public bool IsCompiled => _isCompiled();

    /// <summary>Renders the human-readable diagnostic fact lines for this subscription.</summary>
    public IReadOnlyList<string> Describe() => EventQueryDiagnostics.Describe(this);

    internal void RecordFilterEvaluation() => Interlocked.Increment(ref _filterEvaluations);

    internal void RecordMatch() => Interlocked.Increment(ref _matches);

    internal void RecordDispatch() => Interlocked.Increment(ref _dispatches);

    /// <summary>Removes the subscription from its dispatcher. Safe to call more than once.</summary>
    public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
}
