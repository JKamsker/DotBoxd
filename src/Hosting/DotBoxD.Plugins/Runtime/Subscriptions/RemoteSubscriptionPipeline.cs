using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public sealed class RemoteSubscriptionPipeline<TEvent>
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly RemoteLocalHandlerRegistry? _localHandlers;

    internal RemoteSubscriptionPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        RemoteLocalHandlerRegistry? localHandlers = null)
    {
        _install = install;
        _localHandlers = localHandlers;
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

    /// <summary>
    /// Installs a lowered <c>RunLocal</c> subscription chain: the lowered <c>Where</c>/<c>Select</c>
    /// filter+projection installs server-side and the native <paramref name="handler"/> is registered against
    /// the returned subscription id to receive each filtered, projected value pushed back over IPC.
    /// </summary>
    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, HookContext, ValueTask> handler)
        => InstallLocal(package, handler);

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent, HookContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e));
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        });
    }

    // Decoder overloads: a whole-event RunLocal subscription whose event type is wire-eligible installs with the
    // generated reflection-free decoder, emitted by the interceptor as the 3rd argument.
    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, HookContext, ValueTask> handler, Func<KernelRpcValue, TEvent> decoder)
        => InstallLocal(package, handler, decoder);

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent, HookContext> handler, Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, ValueTask> handler, Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent> handler, Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, HookContext, ValueTask> handler, Func<ReadOnlyMemory<byte>, TEvent> decoder)
        => InstallLocal(package, handler, decoder);

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent, HookContext> handler, Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, ValueTask> handler, Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent> handler, Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    internal RemoteSubscriptionPipeline<TEvent> InstallLocal<TProjected>(PluginPackage package, Func<TProjected, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ValidateSubscription(package);
        if (_localHandlers is null)
        {
            throw LocalHandlersNotSupported();
        }

        var subscriptionId = _install(package).AsTask().GetAwaiter().GetResult();
        _localHandlers.Register(subscriptionId, handler);
        return this;
    }

    internal RemoteSubscriptionPipeline<TEvent> InstallLocal<TProjected>(PluginPackage package, Func<TProjected, HookContext, ValueTask> handler, Func<KernelRpcValue, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(decoder);
        ValidateSubscription(package);
        if (_localHandlers is null)
        {
            throw LocalHandlersNotSupported();
        }

        var subscriptionId = _install(package).AsTask().GetAwaiter().GetResult();
        _localHandlers.Register(subscriptionId, handler, decoder);
        return this;
    }

    internal RemoteSubscriptionPipeline<TEvent> InstallLocal<TProjected>(PluginPackage package, Func<TProjected, HookContext, ValueTask> handler, Func<ReadOnlyMemory<byte>, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(decoder);
        ValidateSubscription(package);
        if (_localHandlers is null)
        {
            throw LocalHandlersNotSupported();
        }

        var subscriptionId = _install(package).AsTask().GetAwaiter().GetResult();
        _localHandlers.Register(subscriptionId, handler, decoder);
        return this;
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
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => throw LocalHandlersNotSupported();

    private static InvalidOperationException NotLowered()
        => new("Remote subscription Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote subscription RunLocal requires an event callback transport; use PluginServer.Subscriptions for local handlers.");

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
}
