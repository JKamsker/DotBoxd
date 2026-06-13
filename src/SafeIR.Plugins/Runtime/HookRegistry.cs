namespace SafeIR.Plugins;

using SafeIR;

public sealed class HookRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object> _pipelines = [];
    private readonly IPluginMessageSink _messages;
    private readonly PluginEventAdapterRegistry _events;
    private readonly KernelRegistry _kernels;

    internal HookRegistry(
        IPluginMessageSink messages,
        PluginEventAdapterRegistry events,
        KernelRegistry kernels)
    {
        _messages = messages;
        _events = events;
        _kernels = kernels;
    }

    public HookPipeline<TEvent> On<TEvent>()
    {
        var adapter = _events.Resolve<TEvent>();
        return On(adapter);
    }

    public HookPipeline<TEvent> On<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        lock (_gate)
        {
            if (_pipelines.TryGetValue(typeof(TEvent), out var existing))
            {
                var pipeline = (HookPipeline<TEvent>)existing;
                if (!pipeline.UsesAdapter(adapter))
                {
                    throw new SandboxValidationException([
                        new SandboxDiagnostic(
                            "SGP034",
                            $"Hook pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
                    ]);
                }

                return pipeline;
            }

            var created = new HookPipeline<TEvent>(adapter, _messages, _kernels);
            _pipelines[typeof(TEvent)] = created;
            return created;
        }
    }

    public async ValueTask PublishAsync<TEvent>(TEvent e, CancellationToken cancellationToken = default)
    {
        object? pipeline;
        lock (_gate)
        {
            _pipelines.TryGetValue(typeof(TEvent), out pipeline);
        }

        if (pipeline is not null)
        {
            await ((HookPipeline<TEvent>)pipeline).PublishAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class HookPipeline<TEvent>
{
    private readonly object _gate = new();

    // Copy-on-write snapshots: publish reads these references without locking or allocating,
    // while mutation replaces the whole array under _gate. Reading each reference once at the
    // start of a publish preserves stable per-publish semantics, because installed delegates
    // never mutate an existing array in place.
    private volatile Func<TEvent, HookContext, ValueTask<bool>>[] _filters = [];
    private volatile Func<TEvent, HookContext, ValueTask>[] _handlers = [];
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly IPluginMessageSink _messages;
    private readonly KernelRegistry _kernels;

    internal HookPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        KernelRegistry kernels)
    {
        _adapter = adapter;
        _messages = messages;
        _kernels = kernels;
    }

    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
        => Where((e, context) => ValueTask.FromResult(filter(e, context)));

    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, ValueTask<bool>> filter)
    {
        lock (_gate)
        {
            _filters = [.. _filters, filter];
        }

        return this;
    }

    public HookPipeline<TEvent> InvokeHostHandler(Func<TEvent, HookContext, ValueTask> handler)
    {
        lock (_gate)
        {
            _handlers = [.. _handlers, handler];
        }

        return this;
    }

    public HookPipeline<TEvent> InvokeHostHandler(Action<TEvent, HookContext> handler)
        => InvokeHostHandler((e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        });

    [Obsolete("Delegate handlers are host-owned code, not plugin kernels. Use UseKernel for plugins or InvokeHostHandler for explicit host handlers.", error: true)]
    public HookPipeline<TEvent> InvokeKernel(Func<TEvent, HookContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    [Obsolete("Delegate handlers are host-owned code, not plugin kernels. Use UseKernel for plugins or InvokeHostHandler for explicit host handlers.", error: true)]
    public HookPipeline<TEvent> InvokeKernel(Action<TEvent, HookContext> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> UseKernel(InstalledKernel kernel)
    {
        kernel.ValidateFor(_adapter);
        return InvokeHostHandler((e, context) => kernel.InvokeAsync(_adapter, e, context.CancellationToken));
    }

    public HookPipeline<TEvent> UseKernel<TKernel>() where TKernel : class
        => UseKernel(_kernels.GetByKernelType<TKernel>());

    internal bool UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => ReferenceEquals(_adapter, adapter);

    internal async ValueTask PublishAsync(TEvent e, CancellationToken cancellationToken)
    {
        // Read each copy-on-write reference once for a stable per-publish snapshot. No lock or
        // allocation: a concurrent mutation replaces the array reference rather than editing it.
        var filters = _filters;
        var handlers = _handlers;

        var context = new HookContext(_messages, cancellationToken);
        foreach (var filter in filters)
        {
            if (!await filter(e, context).ConfigureAwait(false))
            {
                return;
            }
        }

        foreach (var handler in handlers)
        {
            await handler(e, context).ConfigureAwait(false);
        }
    }
}
