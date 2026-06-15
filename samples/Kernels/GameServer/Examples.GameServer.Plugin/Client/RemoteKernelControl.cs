using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Plugin.Client;

internal delegate TReturn RemoteKernelInvocation<TCaptures, TReturn>(
    IGameWorldAccess world,
    TCaptures captures);

internal sealed class RemoteKernelControl
{
    private readonly IGamePluginControlService _control;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _anonymousKernels = new(StringComparer.Ordinal);

    public RemoteKernelControl(IGamePluginControlService control) => _control = control;

    internal IKernelRpcWireClient WireClient => _control;

    /// <summary>
    /// Registers a kernel as the implementation of a server service contract: resolves its generated
    /// verified-IR package and ships it. Returns the installed plugin id.
    /// </summary>
    public async ValueTask<string> Register<TService, TKernel>()
        where TService : class
        where TKernel : class, TService
    {
        var json = PluginPackageJsonSerializer.Export(KernelPackageRegistry.Resolve<TKernel>());
        return await _control.InstallPluginAsync(json).ConfigureAwait(false);
    }

    /// <summary>Strongly-typed settings handle for a kernel this plugin authored.</summary>
    public RemoteKernelHandle<TKernel> Get<TKernel>() where TKernel : class, new()
        => new(_control, PluginId(typeof(TKernel)));

    public ValueTask<TReturn> InvokeAsync<TReturn>(Func<IGameWorldAccess, TReturn> lambda)
    {
        ArgumentNullException.ThrowIfNull(lambda);
        throw new InvalidOperationException(
            "Remote kernel InvokeAsync calls must be intercepted by the DotBoxD plugin generator.");
    }

    public ValueTask<TReturn> InvokeAsync<TCaptures, TReturn>(
        TCaptures captures,
        RemoteKernelInvocation<TCaptures, TReturn> lambda)
        where TCaptures : class
    {
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentNullException.ThrowIfNull(lambda);
        throw new InvalidOperationException(
            "Remote kernel InvokeAsync calls must be intercepted by the DotBoxD plugin generator.");
    }

    internal Task<string> EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> packageFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(packageFactory);
        return _anonymousKernels.GetOrAdd(
            pluginId,
            static (id, args) => new Lazy<Task<string>>(
                () => InstallAnonymousKernelAsync(id, args.PackageFactory, args.Control),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (PackageFactory: packageFactory, Control: _control)).Value;
    }

    internal static string PluginId(Type kernelType)
        => kernelType.GetCustomAttribute<PluginAttribute>()?.Id
           ?? throw new InvalidOperationException($"Kernel '{kernelType.FullName}' has no [Plugin] id.");

    private static async Task<string> InstallAnonymousKernelAsync(
        string pluginId,
        Func<PluginPackage> packageFactory,
        IGamePluginControlService control)
    {
        var json = PluginPackageJsonSerializer.Export(packageFactory());
        var installedPluginId = await control.InstallKernelRpcAsync(json).ConfigureAwait(false);
        if (!string.Equals(installedPluginId, pluginId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Anonymous kernel '{pluginId}' installed as unexpected plugin '{installedPluginId}'.");
        }

        return installedPluginId;
    }
}
