using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public partial class HookPipeline<TEvent, TContext>
{
    /// <summary>
    /// Installs an analyzer-generated hook-chain package and wires it into this pipeline. Called by
    /// the generated interceptor that replaces a <c>Run(lambda)</c> call site, so the lowered
    /// chain runs as verified IR instead of throwing. Blocks on install at setup time.
    /// </summary>
    public HookPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return UseGeneratedChain(package, shouldInvoke: null);
    }

    internal HookPipeline<TEvent, TContext> UseGeneratedChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask<bool>>? shouldInvoke)
    {
        ArgumentNullException.ThrowIfNull(package);
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
            if (shouldInvoke is null)
            {
                return Use(kernel);
            }

            kernel.ValidateFor(_adapter);
            _handlerSet.Add(kernel, async (e, rawContext, context) =>
            {
                if (await shouldInvoke(e, context).ConfigureAwait(false))
                {
                    await kernel.InvokeAsync(_adapter, e, rawContext.CancellationToken).ConfigureAwait(false);
                }
            });
            return this;
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }
    }

    /// <summary>
    /// Wires a lowered <b>local-terminal</b> chain kernel (a remote <c>RunLocal</c> chain): the kernel's
    /// lowered <c>Where</c>/<c>Select</c> always run here in the sandbox, and for each event that passes the
    /// filter the projected value is encoded and handed to <paramref name="push"/> - the control-plane
    /// callback that delivers it across the IPC boundary to the plugin's native delegate. Non-matching events
    /// never reach <paramref name="push"/>, so filtering provably happens server-side before any IPC.
    /// </summary>
    public HookPipeline<TEvent, TContext> UseProjecting(InstalledKernel kernel, string subscriptionId, RemoteLocalPush push)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(push);
        kernel.ValidateFor(_adapter);
        var wholeEvent = LocalCallbackProjection.IsWholeEvent(kernel.Manifest);
        if (wholeEvent)
        {
            LocalCallbackProjection.EnsureWholeEventSupported(_adapter);
        }

        _handlerSet.Add(kernel, (e, rawContext, _) =>
            LocalCallbackProjection.PushAsync(kernel, _adapter, e, rawContext, wholeEvent, subscriptionId, push));

        return this;
    }
}
