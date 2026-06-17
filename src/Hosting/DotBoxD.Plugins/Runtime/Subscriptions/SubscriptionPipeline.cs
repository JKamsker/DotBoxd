using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public sealed class SubscriptionPipeline<TEvent> : IKernelHandlerPipeline
{
    private readonly object _gate = new();
    private volatile Func<TEvent, HookContext, ValueTask<bool>>[] _filters = [];
    private volatile Func<TEvent, HookContext, ValueTask>[] _handlers = [];
    private readonly Dictionary<InstalledKernel, List<Func<TEvent, HookContext, ValueTask>>> _kernelHandlers = [];
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly IPluginMessageSink _messages;
    private readonly HookContext _defaultContext;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;

    internal SubscriptionPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null)
    {
        _adapter = adapter;
        _messages = messages;
        _defaultContext = new HookContext(messages, CancellationToken.None);
        _kernels = kernels;
        _installer = installer;
    }

    public SubscriptionPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_installer is null)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "DBXK065",
                    "this subscription pipeline has no installer; create it from a PluginServer to use generated chains.")
            ]);
        }

        var kernel = _installer(package);
        try
        {
            return Use(kernel);
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }
    }

    public SubscriptionPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
        => Where((e, context) => ValueTask.FromResult(filter(e, context)));

    public SubscriptionPipeline<TEvent> Where(Func<TEvent, HookContext, ValueTask<bool>> filter)
    {
        lock (_gate)
        {
            _filters = [.. _filters, filter];
        }

        return this;
    }

    public SubscriptionPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public SubscriptionPipeline<TEvent> Where(Func<TEvent, ValueTask<bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public SubscriptionPipeline<TEvent> InvokeHostHandler(Func<TEvent, HookContext, ValueTask> handler)
    {
        lock (_gate)
        {
            _handlers = [.. _handlers, handler];
        }

        return this;
    }

    public SubscriptionPipeline<TEvent> InvokeHostHandler(Action<TEvent, HookContext> handler)
        => InvokeHostHandler((e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        });

    public SubscriptionPipeline<TEvent> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public SubscriptionPipeline<TEvent> InvokeHostHandler(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public SubscriptionPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public SubscriptionPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => InvokeHostHandler(handler);

    public SubscriptionPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public SubscriptionPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    public SubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new SubscriptionStage<TEvent, TNext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    public SubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((e, _) => projection(e));
    }

    public SubscriptionPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Run(Action<TEvent> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Use(InstalledKernel kernel)
    {
        kernel.ValidateFor(_adapter);
        var handler = (Func<TEvent, HookContext, ValueTask>)
            ((e, context) => kernel.InvokeAsync(_adapter, e, context.CancellationToken));
        lock (_gate)
        {
            _handlers = [.. _handlers, handler];
            if (!_kernelHandlers.TryGetValue(kernel, out var handlers))
            {
                handlers = [];
                _kernelHandlers[kernel] = handlers;
            }

            handlers.Add(handler);
        }

        return this;
    }

    public SubscriptionPipeline<TEvent> Use<TKernel>() where TKernel : class
        => Use(_kernels.GetByKernelType<TKernel>());

    internal bool UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => ReferenceEquals(_adapter, adapter);

    void IKernelHandlerPipeline.RemoveKernel(InstalledKernel kernel)
        => RemoveKernel(kernel);

    private void RemoveKernel(InstalledKernel kernel)
    {
        lock (_gate)
        {
            if (!_kernelHandlers.Remove(kernel, out var handlers))
            {
                return;
            }

            _handlers = RemoveHandlers(_handlers, handlers);
        }
    }

    private static Func<TEvent, HookContext, ValueTask>[] RemoveHandlers(
        Func<TEvent, HookContext, ValueTask>[] current,
        List<Func<TEvent, HookContext, ValueTask>> removed)
    {
        var next = new List<Func<TEvent, HookContext, ValueTask>>(current.Length);
        foreach (var handler in current)
        {
            if (!removed.Contains(handler))
            {
                next.Add(handler);
            }
        }

        return next.Count == current.Length ? current : [.. next];
    }

    internal void Publish(TEvent e, CancellationToken cancellationToken)
    {
        var filters = _filters;
        var handlers = _handlers;
        if (filters.Length == 0 && handlers.Length == 0)
        {
            return;
        }

        var context = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultContext;
        _ = Task.Run(() => PublishSafelyAsync(filters, handlers, e, context).AsTask());
    }

    private static async ValueTask PublishSafelyAsync(
        Func<TEvent, HookContext, ValueTask<bool>>[] filters,
        Func<TEvent, HookContext, ValueTask>[] handlers,
        TEvent e,
        HookContext context)
    {
        try
        {
            for (var i = 0; i < filters.Length; i++)
            {
                if (!await filters[i](e, context).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch
        {
            return;
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            try
            {
                await handlers[i](e, context).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
