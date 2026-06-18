using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public sealed class RemoteHookPipeline<TEvent>
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly Func<RemoteLocalCallbackRegistration, ValueTask<string>>? _installLocalCallback;

    internal RemoteHookPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        Func<RemoteLocalCallbackRegistration, ValueTask<string>>? installLocalCallback)
    {
        _install = install;
        _installLocalCallback = installLocalCallback;
    }

    public RemoteHookPipeline<TEvent> Use<TKernel>() where TKernel : class
        => UseGeneratedChain(KernelPackageRegistry.Resolve<TKernel>());

    public RemoteHookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateSubscription(package);
        _install(package).AsTask().GetAwaiter().GetResult();
        return this;
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalCallbackChain<TPayload>(
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

    public RemoteHookPipeline<TEvent> UseGeneratedLocalCallbackChain<TPayload>(
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

    public RemoteHookPipeline<TEvent> UseGeneratedLocalCallbackChain<TPayload>(
        PluginPackage package,
        Func<TPayload, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalCallbackChain(package, (TPayload e, HookContext _) => handler(e));
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalCallbackChain<TPayload>(
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

    public RemoteHookPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteHookPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteHookStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext>(this);
    }

    public RemoteHookStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext>(this);
    }

    public RemoteHookPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> Run(Action<TEvent> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => throw NotLowered();

    private static InvalidOperationException NotLowered()
        => new("Remote hook Run/RunLocal lambda calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalCallbacksNotSupported()
        => new("Remote hook RunLocal requires a local callback transport.");

    private static void ValidateSubscription(PluginPackage package)
    {
        var actual = package.Manifest.Subscriptions.Count > 0 ? package.Manifest.Subscriptions[0].Event : null;
        // Manifests now carry the fully-qualified event name; compare against typeof(TEvent).FullName but
        // accept the legacy simple-name form via EventNameMatch for back-compat.
        var expected = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        if (!EventNameMatch.Matches(actual, expected))
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' subscribes to '{actual ?? "<none>"}', not '{expected}'.");
        }
    }

    private static RemoteLocalCallbackPayload Payload<TPayload>(PluginPackage package)
    {
        var handle = package.Module.Functions.FirstOrDefault(
            function => string.Equals(function.Id, package.Entrypoints.Handle, StringComparison.Ordinal));
        if (handle is null)
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' has no callback payload entrypoint '{package.Entrypoints.Handle}'.");
        }

        if (handle.ReturnType == SandboxType.Unit)
        {
            if (typeof(TPayload) != typeof(TEvent))
            {
                throw new InvalidOperationException(
                    $"Hook package '{package.Manifest.PluginId}' sends the original event, not '{typeof(TPayload).FullName}'.");
            }

            return new RemoteLocalCallbackPayload(RemoteLocalCallbackPayloadKind.Event, typeof(TEvent), null);
        }

        return new RemoteLocalCallbackPayload(
            RemoteLocalCallbackPayloadKind.Projection,
            typeof(TPayload),
            handle.Id);
    }
}
