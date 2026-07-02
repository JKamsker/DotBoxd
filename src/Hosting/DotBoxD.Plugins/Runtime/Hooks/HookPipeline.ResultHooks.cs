using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Runtime;

public partial class HookPipeline<TEvent, TContext>
{
    private readonly Hooks.ResultHookSlot<TEvent, TContext> _resultHooks;
    private readonly Dictionary<Type, object> _resultDispatchOptions = [];

    public HookPipeline<TEvent, TContext> Register<TResult>(Func<TEvent, TResult> handler, int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    public HookPipeline<TEvent, TContext> Register<TResult>(
        Func<TEvent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    public HookPipeline<TEvent, TContext> UseGeneratedResultChain<TResult>(PluginPackage package, int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(package);
        var kernel = MaterializeResultKernel(package);
        try
        {
            ValidateResultKernel(kernel, typeof(TResult), resultLocalTerminal: false);
            _resultHooks.AddSandbox(kernel, priority, Hooks.ResultHookSlot<TEvent, TContext>.Decoder<TResult>());
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }

        return this;
    }

    public HookPipeline<TEvent, TContext> UseResult(InstalledKernel kernel, Type resultType, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(resultType);
        EnsureHookResultType(resultType);
        kernel.ValidateFor(_adapter);
        ValidateResultKernel(kernel, resultType, resultLocalTerminal: false);
        _resultHooks.AddSandbox(kernel, priority, Hooks.ResultHookSlot<TEvent, TContext>.Decoder(resultType));
        return this;
    }

    public HookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain<TResult>(package, (e, _) => handler(e), priority);
    }

    public HookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(
            package,
            (e, context, _) => new ValueTask<TResult>(handler(e, context)),
            priority);
    }

    private HookPipeline<TEvent, TContext> UseGeneratedLocalResultChainCore<TResult>(
        PluginPackage package,
        Func<TEvent, TContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        var packageWithCallback = LocalTerminalIdentity.WithCallbackSubscriptionId(
            package,
            LocalTerminalIdentity.CreateCallbackSubscriptionId());
        var kernel = MaterializeResultKernel(packageWithCallback);
        try
        {
            ValidateResultKernel(kernel, typeof(TResult), resultLocalTerminal: true);
            _resultHooks.AddLocal(
                kernel,
                priority,
                async (e, context, ct) => await handler(e, context, ct).ConfigureAwait(false));
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }

        return this;
    }

    public HookPipeline<TEvent, TContext> UseProjectingResult(
        InstalledKernel filterKernel,
        string subscriptionId,
        Type resultType,
        RemoteLocalResultRequest request,
        int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(filterKernel);
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(request);
        EnsureHookResultType(resultType);
        LocalCallbackProjection.EnsureWholeEventSupported(_adapter);
        filterKernel.ValidateFor(_adapter);
        ValidateResultKernel(filterKernel, resultType, resultLocalTerminal: true);
        _resultHooks.AddRemote(filterKernel, priority, async (e, rawContext, _, ct) =>
        {
            var response = await LocalCallbackProjection.RequestResultAsync(
                _adapter,
                e,
                rawContext,
                subscriptionId,
                request,
                ct).ConfigureAwait(false);
            var value = KernelRpcBinaryCodec.DecodeValue(response);
            return (IHookResult)KernelRpcMarshaller.FromKernelRpcValue(value, resultType)!;
        });

        return this;
    }

    public HookPipeline<TEvent, TContext> UseProjectingResult<TResult>(
        InstalledKernel filterKernel,
        string subscriptionId,
        RemoteLocalResultRequest request,
        int priority = 0)
        where TResult : struct, IHookResult
        => UseProjectingResult(filterKernel, subscriptionId, typeof(TResult), request, priority);

    private static void EnsureHookResultType(Type resultType)
    {
        if (resultType.IsValueType && typeof(IHookResult).IsAssignableFrom(resultType))
        {
            return;
        }

        throw new ArgumentException(
            $"Result type '{resultType}' must be a value type implementing {nameof(IHookResult)}.",
            nameof(resultType));
    }

    private static void ValidateResultKernel(InstalledKernel kernel, Type resultType, bool resultLocalTerminal)
    {
        ValidateHookResultContract(resultType);
        ValidateResultManifest(kernel.Manifest, resultType, resultLocalTerminal);
        ValidateHandleReturnType(kernel.Package, resultType, resultLocalTerminal);
    }

    private static void ValidateHookResultContract(Type resultType)
    {
        var hook = (DotBoxD.Abstractions.HookAttribute?)Attribute.GetCustomAttribute(
            typeof(TEvent),
            typeof(DotBoxD.Abstractions.HookAttribute),
            inherit: false);
        if (hook is null || hook.ResultType == resultType)
        {
            return;
        }

        throw ResultValidationError(
            $"Hook context '{typeof(TEvent).FullName}' declares result type " +
            $"'{hook.ResultType.FullName}', but '{resultType.FullName}' was installed.");
    }

    private static void ValidateResultManifest(PluginManifest manifest, Type resultType, bool resultLocalTerminal)
    {
        var foundResultSubscription = false;
        foreach (var subscription in manifest.Subscriptions)
        {
            if (subscription.ResultType is null)
            {
                continue;
            }

            foundResultSubscription = true;
            if (subscription.ResultLocalTerminal != resultLocalTerminal)
            {
                throw ResultValidationError(
                    $"Plugin '{manifest.PluginId}' result subscription declares resultLocalTerminal " +
                    $"'{subscription.ResultLocalTerminal}', but the install path expected '{resultLocalTerminal}'.");
            }

            if (!ResultTypeMatches(subscription.ResultType, resultType))
            {
                throw ResultValidationError(
                    $"Plugin '{manifest.PluginId}' result subscription declares result type " +
                    $"'{subscription.ResultType}', but '{resultType.FullName}' was expected.");
            }
        }

        if (!foundResultSubscription)
        {
            throw ResultValidationError(
                $"Plugin '{manifest.PluginId}' does not declare result hook metadata.");
        }
    }

    private static void ValidateHandleReturnType(
        PluginPackage package,
        Type resultType,
        bool resultLocalTerminal)
    {
        var handle = package.Module.Functions.FirstOrDefault(f =>
            string.Equals(f.Id, package.Entrypoints.Handle, StringComparison.Ordinal));
        if (handle is null)
        {
            return;
        }

        var expected = resultLocalTerminal
            ? SandboxType.Unit
            : KernelRpcMarshaller.HookResultSandboxTypeOf(resultType);
        if (handle.ReturnType.Equals(expected))
        {
            return;
        }

        throw ResultValidationError(
            $"Plugin '{package.Manifest.PluginId}' result Handle returns '{handle.ReturnType}', " +
            $"but '{expected}' was expected.");
    }

    private static bool ResultTypeMatches(string declared, Type expected)
    {
        var expectedName = expected.FullName ?? expected.Name;
        return string.Equals(NormalizeResultTypeName(declared), NormalizeResultTypeName(expectedName), StringComparison.Ordinal);
    }

    private static string NormalizeResultTypeName(string name)
    {
        const string globalPrefix = "global::";
        return (name.StartsWith(globalPrefix, StringComparison.Ordinal)
                ? name[globalPrefix.Length..]
                : name)
            .Replace('+', '.');
    }

    private static SandboxValidationException ResultValidationError(string message)
        => new([new SandboxDiagnostic("DBXK033", message)]);

    private InstalledKernel MaterializeResultKernel(PluginPackage package)
    {
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
            kernel.ValidateFor(_adapter);
            return kernel;
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }
    }
}
