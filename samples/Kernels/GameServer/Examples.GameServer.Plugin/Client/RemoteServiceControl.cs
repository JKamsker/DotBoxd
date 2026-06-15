using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Plugin.Client;

internal delegate TReturn RemoteServerInvocation<TCaptures, TReturn>(
    IGameWorldAccess world,
    TCaptures captures);

internal sealed class RemoteServiceControl
{
    private readonly IGamePluginControlService _control;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _anonymousCalls = new(StringComparer.Ordinal);

    public RemoteServiceControl(IGamePluginControlService control) => _control = control;

    internal IServerExtensionWireClient WireClient => _control;

    public async ValueTask<string> Replace<TService, TKernel>()
        where TService : class
        where TKernel : class, TService
    {
        var json = PluginPackageJsonSerializer.Export(KernelPackageRegistry.Resolve<TKernel>());
        return await _control.InstallPluginAsync(json).ConfigureAwait(false);
    }

    public RemoteKernelHandle<TKernel> Get<TKernel>() where TKernel : class, new()
        => new(_control, PluginId(typeof(TKernel)));

    internal Task<string> EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> packageFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(packageFactory);
        return _anonymousCalls.GetOrAdd(
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
        var installedPluginId = await control.InstallServerExtensionAsync(json).ConfigureAwait(false);
        if (!string.Equals(installedPluginId, pluginId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Anonymous server call '{pluginId}' installed as unexpected plugin '{installedPluginId}'.");
        }

        return installedPluginId;
    }
}
