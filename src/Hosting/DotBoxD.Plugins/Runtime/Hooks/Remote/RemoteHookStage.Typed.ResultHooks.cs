namespace DotBoxD.Plugins.Runtime.Hooks;

public sealed partial class RemoteHookStage<TEvent, TCurrent, TContext>
{
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedResultChain<TResult>(
        PluginPackage package,
        int priority = 0)
        where TResult : struct, IHookResult
        => _root.UseGeneratedResultChain<TResult>(package, priority);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            e => handler((TCurrent)(object)e!),
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            (e, context) => handler((TCurrent)(object)e!, context),
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            e => handler((TCurrent)(object)e!),
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            (e, context) => handler((TCurrent)(object)e!, context),
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            (e, ct) => handler((TCurrent)(object)e!, ct),
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, TContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            (e, context, ct) => handler((TCurrent)(object)e!, context, ct),
            priority);
    }

    [PipelineStep(PipelineStepRole.Register)]
    public RemoteHookPipeline<TEvent, TContext> Register<TResult>(Func<TCurrent, TResult> handler, int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultNotLowered();

    [PipelineStep(PipelineStepRole.Register)]
    public RemoteHookPipeline<TEvent, TContext> Register<TResult>(
        Func<TCurrent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultNotLowered();

    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TCurrent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TCurrent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TCurrent, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TCurrent, TContext, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TCurrent, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RegisterLocal)]
    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TCurrent, TContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    private static InvalidOperationException ResultNotLowered()
        => new("Remote hook Register(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException ResultLocalHandlersNotSupported()
        => new("Remote hook RegisterLocal requires a result callback transport; use PluginServer.Hooks for local result handlers.");
}
