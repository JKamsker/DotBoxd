using System.Collections.Concurrent;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins;

/// <summary>
/// In-process server-extension convenience surface: register a batch kernel under a service contract,
/// then obtain a typed proxy that invokes it request/response. Plugin-facing remote facades usually
/// expose the same capability from the domain surface they extend.
/// </summary>
public sealed partial class PluginServer
{
    private readonly ConcurrentDictionary<Type, ServerExtensionRegistration> _serverExtensions = new();

    /// <summary>
    /// Resolves <typeparamref name="TKernel"/>'s generated verified-IR package, installs it as a server
    /// extension, and binds the <typeparamref name="TService"/> contract to it for
    /// <see cref="ServerExtension{TService}"/>.
    /// </summary>
    public async ValueTask<string> RegisterServerExtensionAsync<TService, TKernel>(
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        where TService : class
        where TKernel : class
    {
        var package = KernelPackageRegistry.Resolve(typeof(TKernel));
        var kernel = await InstallServerExtensionAsync(package, policy, cancellationToken).ConfigureAwait(false);
        _serverExtensions[typeof(TService)] = new ServerExtensionRegistration(kernel.Manifest.PluginId);
        return kernel.Manifest.PluginId;
    }

    /// <summary>
    /// Returns a typed proxy implementing <typeparamref name="TService"/> whose calls run the bound batch
    /// kernel request/response (arguments and results are marshaled to and from the sandbox). Throws if no
    /// extension was registered for the contract.
    /// </summary>
    public TService ServerExtension<TService>() where TService : class
    {
        if (!_serverExtensions.TryGetValue(typeof(TService), out var registration))
        {
            throw new InvalidOperationException(
                $"No server extension is registered for '{typeof(TService)}'. Call RegisterServerExtensionAsync first.");
        }

        return registration.GetProxy<TService>(Kernels);
    }

    private void ClearServerExtensionRegistrations(string pluginId)
    {
        foreach (var pair in _serverExtensions)
        {
            if (string.Equals(pair.Value.PluginId, pluginId, StringComparison.Ordinal))
            {
                _serverExtensions.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class ServerExtensionRegistration(string pluginId)
    {
        private object? _proxy;

        public string PluginId { get; } = pluginId;

        public TService GetProxy<TService>(KernelRegistry kernels) where TService : class
        {
            if (Volatile.Read(ref _proxy) is { } existing)
            {
                return (TService)existing;
            }

            var created = ServerExtensionProxy.Create<TService>(kernels.Get(PluginId));
            var prior = Interlocked.CompareExchange(ref _proxy, created, null);
            return (TService)(prior ?? created);
        }
    }
}
