namespace DotBoxD.Plugins.Runtime;

public sealed partial class RemoteHookPipeline<TEvent>
{
    public RemoteHookPipeline<TEvent> Register<TResult>(Func<TEvent, TResult> handler, int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultNotLowered();

    public RemoteHookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TEvent, HookContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TEvent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent> UseGeneratedResultChain<TResult>(PluginPackage package, int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateSubscription(package);
        ValidateResultSubscription<TResult>(package, resultLocalTerminal: false);
        _install(WithPriority(package, priority)).AsTask().GetAwaiter().GetResult();
        return this;
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, HookContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain<TResult>(
            package,
            (e, context, _) => new ValueTask<TResult>(handler(e, context)),
            priority);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ValidateSubscription(package);
        ValidateResultSubscription<TResult>(package, resultLocalTerminal: true);
        if (_localHandlers is null)
        {
            throw ResultLocalHandlersNotSupported();
        }

        IDisposable? speculativeRegistration = null;
        try
        {
            speculativeRegistration = _localHandlers.RegisterResult(package.Manifest.PluginId, handler);
            var subscriptionId = _install(WithPriority(package, priority)).AsTask().GetAwaiter().GetResult();
            if (!string.Equals(subscriptionId, package.Manifest.PluginId, StringComparison.Ordinal))
            {
                _localHandlers.RegisterResult(subscriptionId, handler);
                speculativeRegistration.Dispose();
                speculativeRegistration = null;
            }
            else
            {
                speculativeRegistration = null;
            }
        }
        finally
        {
            speculativeRegistration?.Dispose();
        }

        return this;
    }

    private static void ValidateResultSubscription<TResult>(PluginPackage package, bool resultLocalTerminal)
        where TResult : struct, IHookResult
    {
        if (package.Manifest.Subscriptions.Count == 0 ||
            package.Manifest.Subscriptions[0] is not { ResultType: { } resultType } subscription)
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' does not declare result hook metadata.");
        }

        if (subscription.ResultLocalTerminal != resultLocalTerminal)
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' result subscription declares resultLocalTerminal " +
                $"'{subscription.ResultLocalTerminal}', but the install path expected '{resultLocalTerminal}'.");
        }

        if (!ResultTypeMatches(resultType, typeof(TResult)))
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' result subscription declares result type " +
                $"'{resultType}', but '{typeof(TResult).FullName}' was expected.");
        }
    }

    private static InvalidOperationException ResultNotLowered()
        => new("Remote hook Register(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException ResultLocalHandlersNotSupported()
        => new("Remote hook RegisterLocal requires a result callback transport; use PluginServer.Hooks for local result handlers.");

    private static PluginPackage WithPriority(PluginPackage package, int priority)
    {
        if (priority == 0)
        {
            return package;
        }

        var subscriptions = new HookSubscriptionManifest[package.Manifest.Subscriptions.Count];
        for (var i = 0; i < subscriptions.Length; i++)
        {
            subscriptions[i] = package.Manifest.Subscriptions[i] with { Priority = priority };
        }

        return package with
        {
            Manifest = package.Manifest with { Subscriptions = subscriptions }
        };
    }
}
