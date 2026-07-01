using System.ComponentModel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime.Subscriptions;

/// <summary>
/// A re-typed stage in a subscription chain after a <c>SubscriptionPipeline&lt;TEvent&gt;.Select</c>.
/// </summary>
[PipelineSurface(PipelineTransport.Local)]
public class SubscriptionStage<TEvent, TCurrent, TContext>
{
    private readonly SubscriptionPipeline<TEvent, TContext> _root;
    private readonly Func<TEvent, TContext, ValueTask<(bool Ok, TCurrent Value)>> _project;

    internal SubscriptionStage(
        SubscriptionPipeline<TEvent, TContext> root,
        Func<TEvent, TContext, ValueTask<(bool Ok, TCurrent Value)>> project)
    {
        _root = root;
        _project = project;
    }

    [PipelineStep(PipelineStepRole.Filter)]
    public SubscriptionStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var project = _project;
        return new SubscriptionStage<TEvent, TCurrent, TContext>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return (ok && filter(value, ctx), value);
        });
    }

    [PipelineStep(PipelineStepRole.Filter)]
    public SubscriptionStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((value, _) => filter(value));
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(Func<TCurrent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var project = _project;
        return new SubscriptionStage<TEvent, TNext, TContext>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return ok ? (true, projection(value, ctx)) : (false, default!);
        });
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(Func<TCurrent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((value, _) => projection(value));
    }

    [PipelineStep(PipelineStepRole.RunLocal)]
    public SubscriptionPipeline<TEvent, TContext> RunLocal(Func<TCurrent, TContext, ValueTask> handler)
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

    [PipelineStep(PipelineStepRole.RunLocal)]
    public SubscriptionPipeline<TEvent, TContext> RunLocal(Action<TCurrent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    [PipelineStep(PipelineStepRole.RunLocal)]
    public SubscriptionPipeline<TEvent, TContext> RunLocal(Func<TCurrent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value));
    }

    [PipelineStep(PipelineStepRole.RunLocal)]
    public SubscriptionPipeline<TEvent, TContext> RunLocal(Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value));
    }

    public SubscriptionPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var project = _project;
        return _root.UseGeneratedChain(package, async (e, ctx) =>
        {
            var (ok, _) = await project(e, ctx).ConfigureAwait(false);
            return ok;
        });
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public SubscriptionPipeline<TEvent, TContext> UseGeneratedChainFromInterceptor(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _root.UseGeneratedChain(package);
    }

    [PipelineStep(PipelineStepRole.Run)]
    public SubscriptionPipeline<TEvent, TContext> Run(Func<TCurrent, TContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public SubscriptionPipeline<TEvent, TContext> Run(Action<TCurrent, TContext> handler)
        => throw HookLowering.NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public SubscriptionPipeline<TEvent, TContext> Run(Func<TCurrent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public SubscriptionPipeline<TEvent, TContext> Run(Action<TCurrent> handler)
        => throw HookLowering.NotLowered();
}
