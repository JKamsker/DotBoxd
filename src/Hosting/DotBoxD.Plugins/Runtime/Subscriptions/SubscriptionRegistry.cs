using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;

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

    internal void EnsureCanRegister<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        lock (_gate)
        {
            if (!_pipelines.TryGetValue(typeof(TEvent), out var existing) ||
                ((SubscriptionPipeline<TEvent>)existing).UsesAdapter(adapter))
            {
                return;
            }

            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "DBXK064",
                    $"Subscription pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
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
