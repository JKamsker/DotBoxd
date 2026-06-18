using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// One registered query within a dispatcher: its filter, equality routing keys, projection, handler, and
/// observability handle. Evaluation is tiered — the portable filter is interpreted until it has been
/// evaluated <see cref="PromotionThreshold"/> times, after which it is compiled once to a delegate and the
/// hot path uses the compiled form. Compilation preserves interpreted semantics (it calls the same reader
/// and comparer); a compile failure falls back to interpretation permanently.
/// </summary>
internal sealed class EventQuerySubscriptionEntry<TEvent>
{
    private const int PromotionThreshold = 16;

    private readonly QueryFilter _filter;
    private Func<object, bool>? _compiled;
    private long _evaluations;

    public EventQuerySubscriptionEntry(
        QueryFilter filter,
        IReadOnlyList<EventQueryRoutingKey> routingKeys,
        Func<TEvent, object?> project,
        Func<object?, HookContext, ValueTask> dispatch,
        EventQuerySubscriptionHandle handle)
    {
        _filter = filter;
        RoutingKeys = routingKeys;
        Project = project;
        Dispatch = dispatch;
        Handle = handle;
    }

    public IReadOnlyList<EventQueryRoutingKey> RoutingKeys { get; }

    public Func<TEvent, object?> Project { get; }

    public Func<object?, HookContext, ValueTask> Dispatch { get; }

    public EventQuerySubscriptionHandle Handle { get; }

    /// <summary>Whether this subscription can be index-routed (has at least one equality key).</summary>
    public bool IsRoutable => RoutingKeys.Count > 0;

    /// <summary>Whether the filter has been promoted to the compiled tier.</summary>
    public bool IsCompiled => Volatile.Read(ref _compiled) is not null;

    /// <summary>Evaluates the filter against <paramref name="e"/>, promoting to the compiled tier when hot.</summary>
    public bool Matches(TEvent e, MemberValueReader reader)
    {
        var compiled = Volatile.Read(ref _compiled);
        if (compiled is not null)
        {
            return compiled(e!);
        }

        if (Interlocked.Increment(ref _evaluations) == PromotionThreshold && TryPromote(reader, out compiled))
        {
            return compiled(e!);
        }

        return QueryFilterEvaluator.Evaluate(_filter, e!, reader);
    }

    private bool TryPromote(MemberValueReader reader, out Func<object, bool> compiled)
    {
        try
        {
            compiled = QueryFilterCompiler.Compile(_filter, reader);
            Volatile.Write(ref _compiled, compiled);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Compilation failed: stay on the interpreter permanently (the threshold check fires only once).
            compiled = null!;
            return false;
        }
    }
}
