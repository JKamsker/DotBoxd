namespace DotBoxD.Plugins.Runtime;

public partial class HookPipeline<TEvent, TContext>
{
    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TContext, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    public HookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(package, (e, _, _) => handler(e), priority);
    }

    public HookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(package, (e, context, _) => handler(e, context), priority);
    }

    public HookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(package, (e, _, ct) => handler(e, ct), priority);
    }

    public HookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(package, handler, priority);
    }
}
