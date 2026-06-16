using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public sealed class SubscriptionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object> _pipelines = [];
    private readonly IPluginMessageSink _messages;
    private readonly PluginEventAdapterRegistry _events;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;

    internal SubscriptionRegistry(
        IPluginMessageSink messages,
        PluginEventAdapterRegistry events,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null)
    {
        _messages = messages;
        _events = events;
        _kernels = kernels;
        _installer = installer;
    }

    public SubscriptionPipeline<TEvent> On<TEvent>()
    {
        var adapter = _events.Resolve<TEvent>();
        return On(adapter);
    }

    public SubscriptionPipeline<TEvent> On<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        lock (_gate)
        {
            if (_pipelines.TryGetValue(typeof(TEvent), out var existing))
            {
                var pipeline = (SubscriptionPipeline<TEvent>)existing;
                if (!pipeline.UsesAdapter(adapter))
                {
                    throw new SandboxValidationException([
                        new SandboxDiagnostic(
                            "DBXK064",
                            $"Subscription pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
                    ]);
                }

                return pipeline;
            }

            var created = new SubscriptionPipeline<TEvent>(adapter, _messages, _kernels, _installer);
            _pipelines[typeof(TEvent)] = created;
            return created;
        }
    }

    public void Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
    {
        object? pipeline;
        lock (_gate)
        {
            _pipelines.TryGetValue(typeof(TEvent), out pipeline);
        }

        ((SubscriptionPipeline<TEvent>?)pipeline)?.Publish(e, cancellationToken);
    }

    public ValueTask PublishAsync<TEvent>(TEvent e, CancellationToken cancellationToken = default)
    {
        Publish(e, cancellationToken);
        return ValueTask.CompletedTask;
    }
}

public sealed class SubscriptionPipeline<TEvent>
{
    private readonly object _gate = new();
    private volatile Func<TEvent, HookContext, ValueTask<bool>>[] _filters = [];
    private volatile Func<TEvent, HookContext, ValueTask>[] _handlers = [];
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

        return Use(_installer(package));
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
        return InvokeHostHandler((e, context) => kernel.InvokeAsync(_adapter, e, context.CancellationToken));
    }

    public SubscriptionPipeline<TEvent> Use<TKernel>() where TKernel : class
        => Use(_kernels.GetByKernelType<TKernel>());

    internal bool UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => ReferenceEquals(_adapter, adapter);

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
