namespace SafeIR.Plugins;

using SafeIR;

public sealed class HookRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object> _pipelines = [];
    private readonly IPluginMessageSink _messages;
    private readonly PluginEventAdapterRegistry _events;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;

    internal HookRegistry(
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

            var created = new HookPipeline<TEvent>(adapter, _messages, _kernels, _installer);
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
    private readonly Func<PluginPackage, InstalledKernel>? _installer;

    internal HookPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null)
    {
        _adapter = adapter;
        _messages = messages;
        _kernels = kernels;
        _installer = installer;
    }

    /// <summary>
    /// Installs an analyzer-generated hook-chain package and wires it into this pipeline. Called by
    /// the generated interceptor that replaces an <c>InvokeKernel(lambda)</c> call site, so the lowered
    /// chain runs as verified IR instead of throwing. Blocks on install at setup time.
    /// </summary>
    public HookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_installer is null)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "SGP063",
                    "this hook pipeline has no installer; create it from a PluginServer to use generated chains.")
            ]);
        }

        return UseKernel(_installer(package));
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

    /// <summary>Native host terminal — runs in-process (NOT sandboxed). Use sparingly.</summary>
    public HookPipeline<TEvent> InvokeLocal(Func<TEvent, HookContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> InvokeLocal(Action<TEvent, HookContext> handler)
        => InvokeHostHandler(handler);

    /// <summary>Projects the flowing element to a new type for downstream Where/terminal stages.</summary>
    public HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    /// <summary>
    /// The terminal the analyzer lowers to verified IR. It never runs as host code: un-lowered it
    /// throws, so plugin logic cannot accidentally execute unsandboxed.
    /// </summary>
    public HookPipeline<TEvent> InvokeKernel(Func<TEvent, HookContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> InvokeKernel(Action<TEvent, HookContext> handler)
        => throw HookLowering.NotLowered();

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

/// <summary>
/// A re-typed stage in a hook chain after a <see cref="HookPipeline{TEvent}.Select{TNext}"/>. It
/// carries a composed projection of the original event to the element currently flowing
/// (<typeparamref name="TCurrent"/>) plus a short-circuit flag, so <c>Where</c>/<c>Select</c> compose
/// without re-keying the pipeline (it stays keyed by <typeparamref name="TEvent"/>). Terminals
/// re-run the projection per publish and short-circuit when a <c>Where</c> rejected the element.
/// </summary>
public sealed class HookStage<TEvent, TCurrent>
{
    private readonly HookPipeline<TEvent> _root;
    private readonly Func<TEvent, HookContext, ValueTask<(bool Ok, TCurrent Value)>> _project;

    internal HookStage(
        HookPipeline<TEvent> root,
        Func<TEvent, HookContext, ValueTask<(bool Ok, TCurrent Value)>> project)
    {
        _root = root;
        _project = project;
    }

    public HookStage<TEvent, TCurrent> Where(Func<TCurrent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var project = _project;
        return new HookStage<TEvent, TCurrent>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return (ok && filter(value, ctx), value);
        });
    }

    public HookStage<TEvent, TNext> Select<TNext>(Func<TCurrent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var project = _project;
        return new HookStage<TEvent, TNext>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return ok ? (true, projection(value, ctx)) : (false, default!);
        });
    }

    /// <summary>Native host terminal over the projected element (NOT sandboxed).</summary>
    public HookPipeline<TEvent> InvokeLocal(Func<TCurrent, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var project = _project;
        return _root.InvokeLocal(async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            if (ok)
            {
                await handler(value, ctx).ConfigureAwait(false);
            }
        });
    }

    public HookPipeline<TEvent> InvokeLocal(Action<TCurrent, HookContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeLocal((value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>The terminal the analyzer lowers to verified IR; un-lowered it throws (never native).</summary>
    public HookPipeline<TEvent> InvokeKernel(Func<TCurrent, HookContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> InvokeKernel(Action<TCurrent, HookContext> handler)
        => throw HookLowering.NotLowered();
}

internal static class HookLowering
{
    public static SandboxValidationException NotLowered()
        => new([
            new SandboxDiagnostic(
                "SGP062",
                "InvokeKernel(lambda) must be lowered to verified IR by SafeIR.PluginAnalyzer and cannot run as host code.")
        ]);
}
