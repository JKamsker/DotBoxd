using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public sealed class HookPipeline<TEvent> : IKernelHandlerPipeline
{
    private readonly object _gate = new();

    // Copy-on-write snapshots: publish reads these references without locking or allocating,
    // while mutation replaces the whole array under _gate. Reading each reference once at the
    // start of a publish preserves stable per-publish semantics, because installed delegates
    // never mutate an existing array in place.
    private volatile Func<TEvent, HookContext, ValueTask<bool>>[] _filters = [];
    private volatile Func<TEvent, HookContext, ValueTask>[] _handlers = [];
    private readonly Dictionary<InstalledKernel, List<Func<TEvent, HookContext, ValueTask>>> _kernelHandlers = [];
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly IPluginMessageSink _messages;
    private readonly HookContext _defaultContext;
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
        _defaultContext = new HookContext(messages, CancellationToken.None);
        _kernels = kernels;
        _installer = installer;
    }

    /// <summary>
    /// Installs an analyzer-generated hook-chain package and wires it into this pipeline. Called by
    /// the generated interceptor that replaces a <c>Run(lambda)</c> call site, so the lowered
    /// chain runs as verified IR instead of throwing. Blocks on install at setup time.
    /// </summary>
    public HookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_installer is null)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "DBXK063",
                    "this hook pipeline has no installer; create it from a PluginServer to use generated chains.")
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

    /// <summary>Element-only filter — the same as the (element, context) overload with the context
    /// discarded. Both arities are always available so a stage need not take the context it doesn't use.</summary>
    public HookPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public HookPipeline<TEvent> Where(Func<TEvent, ValueTask<bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
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

    public HookPipeline<TEvent> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public HookPipeline<TEvent> InvokeHostHandler(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    /// <summary>Native host terminal — runs in-process (NOT sandboxed). Use sparingly.</summary>
    public HookPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    /// <summary>Projects the flowing element to a new type for downstream Where/terminal stages.</summary>
    public HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    public HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((e, _) => projection(e));
    }

    /// <summary>
    /// The terminal the analyzer lowers to verified IR. It never runs as host code: un-lowered it
    /// throws, so plugin logic cannot accidentally execute unsandboxed.
    /// </summary>
    public HookPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Run(Action<TEvent> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Use(InstalledKernel kernel)
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

    public HookPipeline<TEvent> Use<TKernel>() where TKernel : class
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

    internal ValueTask PublishAsync(TEvent e, CancellationToken cancellationToken)
    {
        // Read each copy-on-write reference once for a stable per-publish snapshot. No lock or
        // allocation: a concurrent mutation replaces the array reference rather than editing it.
        var filters = _filters;
        var handlers = _handlers;

        var context = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultContext;
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i](e, context);
            if (!filter.IsCompletedSuccessfully)
            {
                return PublishAfterFilterAwaitAsync(filter, filters, handlers, e, context, i);
            }

            if (!filter.Result)
            {
                return ValueTask.CompletedTask;
            }
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            var handler = handlers[i](e, context);
            if (!handler.IsCompletedSuccessfully)
            {
                return PublishAfterHandlerAwaitAsync(handler, handlers, e, context, i);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static async ValueTask PublishAfterFilterAwaitAsync(
        ValueTask<bool> pending,
        Func<TEvent, HookContext, ValueTask<bool>>[] filters,
        Func<TEvent, HookContext, ValueTask>[] handlers,
        TEvent e,
        HookContext context,
        int index)
    {
        if (!await pending.ConfigureAwait(false))
        {
            return;
        }

        for (var i = index + 1; i < filters.Length; i++)
        {
            if (!await filters[i](e, context).ConfigureAwait(false))
            {
                return;
            }
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            await handlers[i](e, context).ConfigureAwait(false);
        }
    }

    private static async ValueTask PublishAfterHandlerAwaitAsync(
        ValueTask pending,
        Func<TEvent, HookContext, ValueTask>[] handlers,
        TEvent e,
        HookContext context,
        int index)
    {
        await pending.ConfigureAwait(false);
        for (var i = index + 1; i < handlers.Length; i++)
        {
            await handlers[i](e, context).ConfigureAwait(false);
        }
    }
}
