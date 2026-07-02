using System.Text;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// Routes events of one type to matching query subscriptions. Subscriptions with equality predicates are
/// indexed by a <em>composite</em> key over all their equality members, so an event becomes a candidate
/// only when it matches every indexed equality at once (more selective than a single-key index). Each
/// candidate's filter — including any residual/range predicates — is then evaluated (interpreted, promoting
/// to compiled when hot) and, on a match, the projection is materialized and dispatched. Subscriptions with
/// no equality predicate are evaluated against every event (an explicit broad fallback).
/// </summary>
internal sealed class EventQueryDispatcher<TEvent>(MemberValueReader reader)
{
    private readonly object _gate = new();
    private long _eventsObserved;
    private volatile Snapshot _snapshot = Snapshot.Empty;

    public long EventsObserved => Interlocked.Read(ref _eventsObserved);
    public bool HasSubscriptions => !_snapshot.IsEmpty;
    public EventQuerySubscriptionHandle Register(
        EventQueryDocument document,
        EventQueryPlan plan,
        Func<TEvent, object?> project,
        Func<object?, HookContext, ValueTask> dispatch)
    {
        QueryFilterEvaluator.EnsureWithinLimits(document.Filter);
        var fingerprint = QueryFingerprint.Compute(document);
        var routingKeys = RoutingKeysFor(plan);

        EventQuerySubscriptionEntry<TEvent> entry = null!;
        var handle = new EventQuerySubscriptionHandle(
            document, plan, fingerprint, () => EventsObserved, () => entry.IsCompiled, () => Remove(entry));
        entry = new EventQuerySubscriptionEntry<TEvent>(document.Filter, routingKeys, project, dispatch, handle);

        lock (_gate)
        {
            _snapshot = _snapshot.With(entry);
        }

        return handle;
    }

    public async ValueTask PublishAsync(TEvent e, HookContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _eventsObserved);
        var snapshot = _snapshot;
        if (snapshot.IsEmpty || e is null)
        {
            return;
        }

        foreach (var entry in snapshot.Broad)
        {
            await DispatchCandidateAsync(entry, e, context).ConfigureAwait(false);
        }

        foreach (var group in snapshot.Groups)
        {
            if (!Snapshot.TryEventKey(group.Paths, e, reader, out var key) ||
                !group.TryGet(key, out var bucket))
            {
                continue;
            }

            foreach (var entry in bucket)
            {
                await DispatchCandidateAsync(entry, e, context).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask DispatchCandidateAsync(
        EventQuerySubscriptionEntry<TEvent> entry,
        TEvent e,
        HookContext context)
    {
        entry.Handle.RecordFilterEvaluation();
        if (!TryEvaluate(entry, e))
        {
            return;
        }

        entry.Handle.RecordMatch();
        if (!TryProject(entry, e, out var projected))
        {
            return;
        }

        try
        {
            await entry.Dispatch(projected, context).ConfigureAwait(false);
            entry.Handle.RecordDispatch();
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Isolate one subscriber's handler failure so it cannot starve the other dynamic queries
            // matching this event — they share a single forwarding host handler at the registry layer.
        }
    }

    private bool TryEvaluate(EventQuerySubscriptionEntry<TEvent> entry, TEvent e)
    {
        try
        {
            return entry.Matches(e, reader);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryProject(EventQuerySubscriptionEntry<TEvent> entry, TEvent e, out object? projected)
    {
        try
        {
            projected = entry.Project(e);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or NullReferenceException)
        {
            projected = null;
            return false;
        }
    }

    private void Remove(EventQuerySubscriptionEntry<TEvent> entry)
    {
        lock (_gate)
        {
            _snapshot = _snapshot.Without(entry);
        }
    }

    private static IReadOnlyList<EventQueryRoutingKey> RoutingKeysFor(EventQueryPlan plan)
    {
        if (plan.RoutingKeys.Count == 0)
        {
            return [];
        }

        var keys = new List<EventQueryRoutingKey>(plan.RoutingKeys.Count);
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var predicate in plan.RoutingKeys)
        {
            if (seenPaths.Add(predicate.Path))
            {
                keys.Add(EventQueryRoutingKey.FromValue(predicate.Path, predicate.Value));
            }
        }

        return keys;
    }

    private sealed class Snapshot
    {
        public static readonly Snapshot Empty = new([]);

        private const string Separator = "\u0001";

        private readonly EventQuerySubscriptionEntry<TEvent>[] _all;
        private readonly EventQuerySubscriptionEntry<TEvent>[] _broad;
        private readonly RoutingGroup[] _groups;

        private Snapshot(EventQuerySubscriptionEntry<TEvent>[] all)
        {
            _all = all;
            var broad = new List<EventQuerySubscriptionEntry<TEvent>>();
            var builders = new Dictionary<string, RoutingGroup>(StringComparer.Ordinal);
            foreach (var entry in all)
            {
                if (!entry.IsRoutable)
                {
                    broad.Add(entry);
                    continue;
                }

                var paths = entry.RoutingKeys
                    .Select(k => k.Path)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToArray();

                var groupKey = string.Join(Separator, paths);
                if (!builders.TryGetValue(groupKey, out var group))
                {
                    group = new RoutingGroup(paths);
                    builders[groupKey] = group;
                }

                group.Add(CompositeKey(entry, paths), entry);
            }

            _broad = [.. broad];
            _groups = [.. builders.Values];
        }

        public bool IsEmpty => _all.Length == 0;

        public EventQuerySubscriptionEntry<TEvent>[] Broad => _broad;

        public RoutingGroup[] Groups => _groups;

        public Snapshot With(EventQuerySubscriptionEntry<TEvent> entry) => new([.. _all, entry]);

        public Snapshot Without(EventQuerySubscriptionEntry<TEvent> entry)
            => new(_all.Where(e => !ReferenceEquals(e, entry)).ToArray());

        // Reused on the hot TryEventKey path; nested same-thread calls allocate their own builder.
        [ThreadStatic] private static StringBuilder? _eventKeyBuilder;
        [ThreadStatic] private static bool _eventKeyBuilderInUse;

        private static string CompositeKey(EventQuerySubscriptionEntry<TEvent> entry, string[] sortedPaths)
        {
            var builder = new StringBuilder();
            foreach (var path in sortedPaths)
            {
                var key = entry.RoutingKeys.First(k => k.Path == path);
                key.AppendValueToken(builder);
                builder.Append(Separator);
            }

            return builder.ToString();
        }

        public static bool TryEventKey(string[] sortedPaths, TEvent e, MemberValueReader reader, out string key)
        {
            var reuseThreadBuilder = !_eventKeyBuilderInUse;
            var builder = reuseThreadBuilder ? _eventKeyBuilder ??= new StringBuilder() : new StringBuilder();
            if (reuseThreadBuilder)
            {
                _eventKeyBuilderInUse = true;
            }

            try
            {
                builder.Clear();
                foreach (var path in sortedPaths)
                {
                    if (!EventQueryRoutingKey.TryFromRuntime(path, reader.Read(e!, path), out var runtimeKey))
                    {
                        key = string.Empty;
                        return false;
                    }

                    runtimeKey.AppendValueToken(builder);
                    builder.Append(Separator);
                }

                key = builder.ToString();
                return true;
            }
            catch (InvalidOperationException)
            {
                key = string.Empty;
                return false;
            }
            finally
            {
                if (reuseThreadBuilder)
                {
                    builder.Clear();
                    _eventKeyBuilderInUse = false;
                }
            }
        }
    }

    private sealed class RoutingGroup(string[] paths)
    {
        private readonly Dictionary<string, List<EventQuerySubscriptionEntry<TEvent>>> _byValue =
            new(StringComparer.Ordinal);

        public string[] Paths { get; } = paths;

        public void Add(string compositeKey, EventQuerySubscriptionEntry<TEvent> entry)
        {
            if (!_byValue.TryGetValue(compositeKey, out var bucket))
            {
                bucket = [];
                _byValue[compositeKey] = bucket;
            }

            bucket.Add(entry);
        }

        public bool TryGet(string compositeKey, out List<EventQuerySubscriptionEntry<TEvent>> bucket)
            => _byValue.TryGetValue(compositeKey, out bucket!);
    }
}
