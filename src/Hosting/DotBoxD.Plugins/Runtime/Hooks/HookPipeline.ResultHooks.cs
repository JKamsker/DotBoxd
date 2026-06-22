using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Runtime;

// The result-returning hook surface of HookPipeline<TEvent>: the .Register(...) / .RegisterLocal(...) terminals
// (lowered by the analyzer), the generated install entrypoints the interceptors call, and FireResultAsync the
// host calls to dispatch. The actual ordered dispatch + abstain/fallthrough logic lives in ResultHookSlot; this
// partial is the thin pipeline facade over it, kept separate so the notification surface stays focused.
public sealed partial class HookPipeline<TEvent>
{
    private readonly Hooks.ResultHookSlot<TEvent> _resultHooks;

    /// <summary>
    /// The result-returning terminal the analyzer lowers to verified IR: the filter and the result-producing
    /// handler both run in the sandbox. Un-lowered it throws, so plugin logic never executes unsandboxed.
    /// </summary>
    public HookPipeline<TEvent> Register<TResult>(Func<TEvent, TResult> handler, int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    /// <summary>
    /// The result-returning local terminal: the analyzer lowers the filter to verified IR, but the result is
    /// produced by the plugin-process delegate. Un-lowered it throws; the generated interceptor replaces it.
    /// </summary>
    public HookPipeline<TEvent> RegisterLocal<TResult>(Func<TEvent, HookContext, TResult> handler, int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    public HookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TEvent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw Hooks.HookLowering.ResultNotLowered();

    /// <summary>
    /// Installs a lowered <c>Register</c> chain: the package's verified <c>ShouldHandle</c> filter and
    /// result-producing <c>Handle</c> run in the sandbox, and the returned value is decoded to
    /// <typeparamref name="TResult"/>. Called by the generated interceptor that replaces a
    /// <c>Register(lambda, priority)</c> call site.
    /// </summary>
    public HookPipeline<TEvent> UseGeneratedResultChain<TResult>(PluginPackage package, int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(package);
        var kernel = MaterializeResultKernel(package);
        try
        {
            ValidateResultKernel(kernel, typeof(TResult), resultLocalTerminal: false);
            _resultHooks.AddSandbox(kernel, priority, Hooks.ResultHookSlot<TEvent>.Decoder<TResult>());
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }

        return this;
    }

    public HookPipeline<TEvent> UseResult(InstalledKernel kernel, Type resultType, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(resultType);
        EnsureHookResultType(resultType);
        kernel.ValidateFor(_adapter);
        ValidateResultKernel(kernel, resultType, resultLocalTerminal: false);
        _resultHooks.AddSandbox(kernel, priority, Hooks.ResultHookSlot<TEvent>.Decoder(resultType));
        return this;
    }

    /// <summary>
    /// Installs a lowered <c>RegisterLocal</c> chain: the package's verified <c>ShouldHandle</c> filter runs in
    /// the sandbox, and only when it matches is the native <paramref name="handler"/> invoked to produce the
    /// result. Called by the generated interceptor that replaces a <c>RegisterLocal(lambda, priority)</c> site.
    /// </summary>
    public HookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, HookContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain<TResult>(package, (e, context, _) => new ValueTask<TResult>(handler(e, context)), priority);
    }

    public HookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        var kernel = MaterializeResultKernel(package);
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

    public HookPipeline<TEvent> UseProjectingResult(
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
        _resultHooks.AddRemote(filterKernel, priority, async (e, context, ct) =>
        {
            var response = await LocalCallbackProjection.RequestResultAsync(
                _adapter,
                e,
                context,
                subscriptionId,
                request,
                ct).ConfigureAwait(false);
            var value = KernelRpcBinaryCodec.DecodeValue(response);
            return (IHookResult)KernelRpcMarshaller.FromKernelRpcValue(value, resultType)!;
        });

        return this;
    }

    public HookPipeline<TEvent> UseProjectingResult<TResult>(
        InstalledKernel filterKernel,
        string subscriptionId,
        RemoteLocalResultRequest request,
        int priority = 0)
        where TResult : struct, IHookResult
        => UseProjectingResult(filterKernel, subscriptionId, typeof(TResult), request, priority);

    /// <summary>
    /// Dispatches result hooks for <paramref name="e"/> in descending priority order and returns the first
    /// successful result, or <see langword="null"/> when none is registered or none succeeds. The host applies
    /// the returned result to its live state.
    /// </summary>
    public ValueTask<TResult?> FireResultAsync<TResult>(TEvent e, CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
        => FireResultAsync(e, ResultHookDispatchOptions<TResult>.Default, cancellationToken);

    public ValueTask<TResult?> FireResultAsync<TResult>(
        TEvent e,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
    {
        if (!_resultHooks.HasHandlers)
        {
            return new ValueTask<TResult?>((TResult?)null);
        }

        var context = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultContext;
        return _resultHooks.FireAsync(e, context, options, cancellationToken);
    }

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
        ValidateResultManifest(kernel.Manifest, resultType, resultLocalTerminal);
        ValidateHandleReturnType(kernel.Package, resultType, resultLocalTerminal);
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
            : KernelRpcMarshaller.SandboxTypeOf(resultType);
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
