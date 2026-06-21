using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime;

internal sealed class KernelHandlerSet<TEvent>
{
    private readonly object _gate = new();
    private volatile Func<TEvent, HookContext, ValueTask>[] _handlers = [];
    private readonly Dictionary<InstalledKernel, List<Func<TEvent, HookContext, ValueTask>>> _kernelHandlers = [];
    private readonly Dictionary<InstalledKernelPool, List<Func<TEvent, HookContext, ValueTask>>> _poolHandlers = [];

    public Func<TEvent, HookContext, ValueTask>[] Snapshot => _handlers;

    public void Add(Func<TEvent, HookContext, ValueTask> handler)
    {
        lock (_gate)
        {
            _handlers = [.. _handlers, handler];
        }
    }

    public void Add(InstalledKernel kernel, Func<TEvent, HookContext, ValueTask> handler)
    {
        lock (_gate)
        {
            AddCore(handler);
            if (!_kernelHandlers.TryGetValue(kernel, out var handlers))
            {
                handlers = [];
                _kernelHandlers[kernel] = handlers;
            }

            handlers.Add(handler);
        }
    }

    public void Add(InstalledKernelPool pool, Func<TEvent, HookContext, ValueTask> handler)
    {
        lock (_gate)
        {
            AddCore(handler);
            if (!_poolHandlers.TryGetValue(pool, out var handlers))
            {
                handlers = [];
                _poolHandlers[pool] = handlers;
            }

            handlers.Add(handler);
        }
    }

    public void Remove(InstalledKernel kernel)
    {
        lock (_gate)
        {
            // Only individually-registered kernels (via Add(kernel, ...)) are removable here. A kernel
            // that is merely a pool member was never added on its own, so removal is a no-op; pools are
            // detached as a unit through Remove(InstalledKernelPool).
            if (_kernelHandlers.Remove(kernel, out var handlers))
            {
                _handlers = RemoveHandlers(_handlers, handlers);
            }
        }
    }

    public void Remove(InstalledKernelPool pool)
    {
        lock (_gate)
        {
            RemoveCore(pool);
        }
    }

    private void AddCore(Func<TEvent, HookContext, ValueTask> handler)
        => _handlers = [.. _handlers, handler];

    private void RemoveCore(InstalledKernelPool pool)
    {
        if (_poolHandlers.Remove(pool, out var handlers))
        {
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
}
