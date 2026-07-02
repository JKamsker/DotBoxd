using System.Collections.Concurrent;
using System.Linq.Expressions;
using DotBoxD.Queryable.Analysis;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// An in-process <see cref="IEventQuerySource"/>: it translates authored queries into the portable AST,
/// plans them, and dispatches events to matching subscriptions through a per-event-type indexed
/// <see cref="EventQueryDispatcher{TEvent}"/>. Feed it events with <see cref="PublishAsync{TEvent}"/>; a
/// host wires that call to its own event source (for example a subscription registry).
/// </summary>
public sealed class EventQueryHost : IEventQuerySource
{
    private readonly MemberValueReader _reader = new();
    private readonly object _gate = new();
    // Read lock-free on the hot PublishAsync/HasSubscriptions path; the dispatcher set only mutates on
    // Register, which still serializes through _gate so each event type creates exactly one dispatcher.
    private readonly ConcurrentDictionary<Type, object> _dispatchers = new();

    /// <inheritdoc />
    public EventQuery<TEvent> Query<TEvent>() => new(this);

    /// <summary>Routes an event to the matching query subscriptions registered for its type.</summary>
    public ValueTask PublishAsync<TEvent>(TEvent e, HookContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        return TryGetDispatcher<TEvent>() is { } dispatcher
            ? dispatcher.PublishAsync(e, context)
            : ValueTask.CompletedTask;
    }

    /// <summary>Whether any subscription has been registered for <typeparamref name="TEvent"/>.</summary>
    public bool HasSubscriptions<TEvent>() => TryGetDispatcher<TEvent>() is not null;

    internal EventQuerySubscriptionHandle Register<TEvent, TProjection>(
        IReadOnlyList<Expression<Func<TEvent, bool>>> predicates,
        Expression<Func<TEvent, TProjection>>? projection,
        Func<TProjection, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        ArgumentNullException.ThrowIfNull(handler);

        var filter = BuildFilter(predicates);
        QuerySatisfiability.EnsureSatisfiable(filter);
        var (projectionAst, project) = BuildProjection(projection);
        var document = EventQueryDocument.Create(ExpressionQueryTranslator.EventName<TEvent>(), filter, projectionAst);
        var plan = EventQueryPlanner.Plan(document);

        ValueTask Dispatch(object? projected, HookContext context) => handler((TProjection)projected!, context);
        return GetOrAddDispatcher<TEvent>().Register(document, plan, project, Dispatch);
    }

    private static QueryFilter BuildFilter<TEvent>(IReadOnlyList<Expression<Func<TEvent, bool>>> predicates)
    {
        if (predicates.Count == 0)
        {
            return QueryFilter.MatchAll;
        }

        var filters = new QueryFilter[predicates.Count];
        for (var i = 0; i < predicates.Count; i++)
        {
            filters[i] = ExpressionQueryTranslator.TranslateFilter(predicates[i]);
        }

        return QueryFilter.And(filters);
    }

    private static (QueryProjection Ast, Func<TEvent, object?> Project) BuildProjection<TEvent, TProjection>(
        Expression<Func<TEvent, TProjection>>? projection)
    {
        if (projection is null)
        {
            return (QueryProjection.Identity, e => e);
        }

        var ast = ExpressionQueryTranslator.TranslateProjection(projection);
        var compiled = projection.Compile();
        return (ast, e => compiled(e));
    }

    private EventQueryDispatcher<TEvent> GetOrAddDispatcher<TEvent>()
    {
        // Double-checked under _gate (not ConcurrentDictionary.GetOrAdd): the value factory can run on
        // racing threads and discard a built dispatcher, which would silently drop a concurrent Register's
        // subscription. The lock guarantees one dispatcher instance per type and that the caller registers
        // onto the instance that is actually stored.
        lock (_gate)
        {
            if (!_dispatchers.TryGetValue(typeof(TEvent), out var existing))
            {
                existing = new EventQueryDispatcher<TEvent>(_reader);
                _dispatchers[typeof(TEvent)] = existing;
            }

            return (EventQueryDispatcher<TEvent>)existing;
        }
    }

    private EventQueryDispatcher<TEvent>? TryGetDispatcher<TEvent>()
    {
        if (!_dispatchers.TryGetValue(typeof(TEvent), out var existing))
        {
            return null;
        }

        var dispatcher = (EventQueryDispatcher<TEvent>)existing;
        return dispatcher.HasSubscriptions ? dispatcher : null;
    }
}
