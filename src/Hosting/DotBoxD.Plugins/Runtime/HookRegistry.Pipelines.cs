using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime;

public sealed partial class HookRegistry
{
    private void EnsureCanRegisterLocked<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        foreach (var (key, existing) in _pipelines)
        {
            if (key.EventType == typeof(TEvent) &&
                !((IHookPipeline<TEvent>)existing).UsesAdapter(adapter))
            {
                throw new SandboxValidationException([
                    new SandboxDiagnostic(
                        "DBXK034",
                        $"Hook pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
                ]);
            }
        }
    }

    private static void EnsureContextFactoryMatches<TContext>(
        Func<Func<HookContext, TContext>, bool> usesContextFactory,
        Func<HookContext, TContext> createContext,
        string surface)
    {
        if (usesContextFactory(createContext))
        {
            return;
        }

        throw new SandboxValidationException([
            new SandboxDiagnostic(
                "DBXK067",
                $"A {surface} pipeline for context '{typeof(TContext).Name}' is already registered with a different context factory.")
        ]);
    }

    private (object? Single, object[]? Multiple) PipelinesForEventLocked<TEvent>()
    {
        var eventType = typeof(TEvent);
        if (!_pipelineEventTypes.Contains(eventType))
        {
            return (null, null);
        }

        if (_pipelineFanout.TryGetValue(eventType, out var cached))
        {
            return cached;
        }

        object? single = null;
        List<object>? multiple = null;
        foreach (var (key, pipeline) in _pipelines)
        {
            if (key.EventType != eventType)
            {
                continue;
            }

            if (single is null && multiple is null)
            {
                single = pipeline;
                continue;
            }

            multiple ??= [single!];
            single = null;
            multiple.Add(pipeline);
        }

        (object? Single, object[]? Multiple) fanout = multiple is null
            ? (single, null)
            : (null, multiple.ToArray());
        _pipelineFanout[eventType] = fanout;
        return fanout;
    }

    private void RegisterEventTypeLocked<TEvent>()
    {
        var eventType = typeof(TEvent);
        _pipelineEventTypes.Add(eventType);
        _pipelineFanout.Remove(eventType);
    }

    private static async ValueTask PublishManyAsync<TEvent>(
        object[] pipelines,
        TEvent e,
        CancellationToken cancellationToken)
    {
        foreach (var pipeline in pipelines)
        {
            await ((IHookPipeline<TEvent>)pipeline).PublishAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<TResult?> FireManyAsync<TEvent, TResult>(
        object[] pipelines,
        TEvent e,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        var registrations = OrderedResultRegistrations<TEvent>(pipelines);
        foreach (var registration in registrations)
        {
            var result = await registration
                .InvokeAsync<TResult>(e, options: null, cancellationToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static async ValueTask<TResult?> FireManyAsync<TEvent, TResult>(
        object[] pipelines,
        TEvent e,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        var registrations = OrderedResultRegistrations<TEvent>(pipelines);
        foreach (var registration in registrations)
        {
            var result = await registration
                .InvokeAsync(e, options, cancellationToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static Hooks.IResultHookRegistration<TEvent>[] OrderedResultRegistrations<TEvent>(object[] pipelines)
    {
        var registrations = new List<Hooks.IResultHookRegistration<TEvent>>();
        foreach (var pipeline in pipelines)
        {
            registrations.AddRange(((IHookPipeline<TEvent>)pipeline).ResultRegistrations());
        }

        registrations.Sort(static (left, right) => left.Priority != right.Priority
            ? right.Priority.CompareTo(left.Priority)
            : left.Order.CompareTo(right.Order));
        return [.. registrations];
    }
}
