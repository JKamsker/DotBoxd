using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public sealed class RemoteSubscriptionPipeline<TEvent>
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly Func<RemoteLocalCallbackRegistration, ValueTask<string>>? _installLocalCallback;

    internal RemoteSubscriptionPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        Func<RemoteLocalCallbackRegistration, ValueTask<string>>? installLocalCallback)
    {
        _install = install;
        _installLocalCallback = installLocalCallback;
    }

    public RemoteSubscriptionPipeline<TEvent> Use<TKernel>() where TKernel : class
        => UseGeneratedChain(KernelPackageRegistry.Resolve<TKernel>());

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateSubscription(package);
        _install(package).AsTask().GetAwaiter().GetResult();
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalCallbackChain<TPayload>(
        PluginPackage package,
        Func<TPayload, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        if (_installLocalCallback is null)
        {
            throw LocalCallbacksNotSupported();
        }

        ValidateSubscription(package);
        _installLocalCallback(new RemoteLocalCallbackRegistration(
                typeof(TEvent),
                Payload<TPayload>(package),
                package,
                handler))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalCallbackChain<TPayload>(
        PluginPackage package,
        Action<TPayload, HookContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalCallbackChain(package, (TPayload e, HookContext context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalCallbackChain<TPayload>(
        PluginPackage package,
        Func<TPayload, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalCallbackChain(package, (TPayload e, HookContext _) => handler(e));
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalCallbackChain<TPayload>(
        PluginPackage package,
        Action<TPayload> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalCallbackChain(package, (TPayload e, HookContext _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(this);
    }

    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(this);
    }

    public RemoteSubscriptionPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> Run(Action<TEvent> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => throw NotLowered();

    private static InvalidOperationException NotLowered()
        => new("Remote subscription Run/RunLocal lambda calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalCallbacksNotSupported()
        => new("Remote subscription RunLocal requires a local callback transport.");

    private static void ValidateSubscription(PluginPackage package)
    {
        var actual = package.Manifest.Subscriptions.Count > 0 ? package.Manifest.Subscriptions[0].Event : null;
        // Manifests now carry the fully-qualified event name; compare against typeof(TEvent).FullName but
        // accept the legacy simple-name form via EventNameMatch for back-compat.
        var expected = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        if (!EventNameMatch.Matches(actual, expected))
        {
            throw new InvalidOperationException(
                $"Subscription package '{package.Manifest.PluginId}' subscribes to '{actual ?? "<none>"}', not '{expected}'.");
        }
    }

    private static RemoteLocalCallbackPayload Payload<TPayload>(PluginPackage package)
    {
        var handle = package.Module.Functions.FirstOrDefault(
            function => string.Equals(function.Id, package.Entrypoints.Handle, StringComparison.Ordinal));
        if (handle is null)
        {
            throw new InvalidOperationException(
                $"Subscription package '{package.Manifest.PluginId}' has no callback payload entrypoint '{package.Entrypoints.Handle}'.");
        }

        if (handle.ReturnType == SandboxType.Unit)
        {
            if (typeof(TPayload) != typeof(TEvent))
            {
                throw new InvalidOperationException(
                    $"Subscription package '{package.Manifest.PluginId}' sends the original event, not '{typeof(TPayload).FullName}'.");
            }

            return new RemoteLocalCallbackPayload(RemoteLocalCallbackPayloadKind.Event, typeof(TEvent), null);
        }

        return new RemoteLocalCallbackPayload(
            RemoteLocalCallbackPayloadKind.Projection,
            typeof(TPayload),
            handle.Id);
    }
}
