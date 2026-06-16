using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime.Subscriptions;

/// <summary>
/// A re-typed stage in a subscription chain after a <c>SubscriptionPipeline&lt;TEvent&gt;.Select</c>.
/// </summary>
public sealed class SubscriptionStage<TEvent, TCurrent>
{
    private readonly SubscriptionPipeline<TEvent> _root;
    private readonly Func<TEvent, HookContext, ValueTask<(bool Ok, TCurrent Value)>> _project;

    internal SubscriptionStage(
        SubscriptionPipeline<TEvent> root,
        Func<TEvent, HookContext, ValueTask<(bool Ok, TCurrent Value)>> project)
    {
        _root = root;
        _project = project;
    }

    public SubscriptionStage<TEvent, TCurrent> Where(Func<TCurrent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var project = _project;
        return new SubscriptionStage<TEvent, TCurrent>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return (ok && filter(value, ctx), value);
        });
    }

    public SubscriptionStage<TEvent, TCurrent> Where(Func<TCurrent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((value, _) => filter(value));
    }

    public SubscriptionStage<TEvent, TNext> Select<TNext>(Func<TCurrent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var project = _project;
        return new SubscriptionStage<TEvent, TNext>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return ok ? (true, projection(value, ctx)) : (false, default!);
        });
    }

    public SubscriptionStage<TEvent, TNext> Select<TNext>(Func<TCurrent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((value, _) => projection(value));
    }

    public SubscriptionPipeline<TEvent> RunLocal(Func<TCurrent, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var project = _project;
        return _root.RunLocal(async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            if (ok)
            {
                await handler(value, ctx).ConfigureAwait(false);
            }
        });
    }

    public SubscriptionPipeline<TEvent> RunLocal(Action<TCurrent, HookContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public SubscriptionPipeline<TEvent> RunLocal(Func<TCurrent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value));
    }

    public SubscriptionPipeline<TEvent> RunLocal(Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value));
    }

    public SubscriptionPipeline<TEvent> UseGeneratedChain(PluginPackage package)
        => _root.UseGeneratedChain(package);

    public SubscriptionPipeline<TEvent> Run(Func<TCurrent, HookContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Run(Action<TCurrent, HookContext> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Run(Func<TCurrent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Run(Action<TCurrent> handler)
        => throw HookLowering.NotLowered();
}
