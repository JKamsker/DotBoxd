using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime;

internal interface IKernelHandlerPipeline
{
    void RemoveKernel(InstalledKernel kernel);
    void RemoveKernelPool(InstalledKernelPool pool);
}

public sealed class HookRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object> _pipelines = [];
    private readonly IPluginMessageSink _messages;
    private readonly PluginEventAdapterRegistry _events;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;
    private readonly Action<ResultHookFault>? _onFault;

    internal HookRegistry(
        IPluginMessageSink messages,
        PluginEventAdapterRegistry events,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<ResultHookFault>? onFault = null)
    {
        _messages = messages;
        _events = events;
        _kernels = kernels;
        _installer = installer;
        _onFault = onFault;
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
                            "DBXK034",
                            $"Hook pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
                    ]);
                }

                return pipeline;
            }

            var created = new HookPipeline<TEvent>(adapter, _messages, _kernels, _installer, _onFault);
            _pipelines[typeof(TEvent)] = created;
            return created;
        }
    }

    internal void EnsureCanRegister<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        lock (_gate)
        {
            if (!_pipelines.TryGetValue(typeof(TEvent), out var existing) ||
                ((HookPipeline<TEvent>)existing).UsesAdapter(adapter))
            {
                return;
            }

            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "DBXK034",
                    $"Hook pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
            ]);
        }
    }

    internal void RemoveKernel(InstalledKernel kernel)
    {
        object[] pipelines;
        lock (_gate)
        {
            pipelines = [.. _pipelines.Values];
        }

        foreach (var pipeline in pipelines)
        {
            ((IKernelHandlerPipeline)pipeline).RemoveKernel(kernel);
        }
    }

    internal void RemoveKernelPool(InstalledKernelPool pool)
    {
        object[] pipelines;
        lock (_gate)
        {
            pipelines = [.. _pipelines.Values];
        }

        foreach (var pipeline in pipelines)
        {
            ((IKernelHandlerPipeline)pipeline).RemoveKernelPool(pool);
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

    /// <summary>
    /// Dispatches the result-returning hooks (<c>.Register(...)</c> / <c>.RegisterLocal(...)</c>) installed for
    /// <typeparamref name="TContext"/> in descending priority order, returning the first successful
    /// <typeparamref name="TResult"/> or <see langword="null"/> when none is registered or none succeeds. The
    /// host applies the returned result to its live state. The result type is named explicitly here because the
    /// host — unlike the plugin authoring side, where it is inferred from the <c>[Hook]</c> context — already
    /// knows the type it will apply.
    /// </summary>
    public ValueTask<TResult?> FireAsync<TContext, TResult>(TContext context, CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
    {
        object? pipeline;
        lock (_gate)
        {
            _pipelines.TryGetValue(typeof(TContext), out pipeline);
        }

        ValidateResultType<TContext, TResult>();
        if (pipeline is null)
        {
            return new ValueTask<TResult?>((TResult?)null);
        }

        return ((HookPipeline<TContext>)pipeline).FireResultAsync<TResult>(context, cancellationToken);
    }

    private static void ValidateResultType<TContext, TResult>()
        where TResult : struct, IHookResult
    {
        var hook = (HookAttribute?)Attribute.GetCustomAttribute(
            typeof(TContext),
            typeof(HookAttribute),
            inherit: false);
        if (hook is null || hook.ResultType == typeof(TResult))
        {
            return;
        }

        throw new SandboxValidationException([
            new SandboxDiagnostic(
                "DBXK066",
                $"Hook context '{typeof(TContext).Name}' declares result type '{hook.ResultType.Name}', " +
                $"but FireAsync was called with '{typeof(TResult).Name}'.")
        ]);
    }
}
