namespace SafeIR.Plugins;

using System.Collections.Concurrent;

/// <summary>
/// In-process kernel RPC service convenience surface: register a batch kernel under a service contract,
/// then obtain a typed proxy that invokes it request/response. This is the in-process form of the
/// followup's <c>server.RegisterKernelRpcService&lt;TService, TKernel&gt;()</c> /
/// <c>server.KernelRpcService&lt;TService&gt;()</c>; over IPC the same shape is forwarded by a remote
/// facade (see the GameServer example).
/// </summary>
public sealed partial class PluginServer
{
    private readonly ConcurrentDictionary<Type, string> _rpcServices = new();

    /// <summary>
    /// Resolves <typeparamref name="TKernel"/>'s generated verified-IR package, installs it as a kernel
    /// RPC service, and binds the <typeparamref name="TService"/> contract to it for
    /// <see cref="RpcService{TService}"/>.
    /// </summary>
    public async ValueTask<string> RegisterRpcServiceAsync<TService, TKernel>(
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        where TService : class
        where TKernel : class
    {
        var package = KernelPackageRegistry.Resolve(typeof(TKernel));
        var kernel = await InstallRpcAsync(package, policy, cancellationToken).ConfigureAwait(false);
        _rpcServices[typeof(TService)] = kernel.Manifest.PluginId;
        return kernel.Manifest.PluginId;
    }

    /// <summary>
    /// Returns a typed proxy implementing <typeparamref name="TService"/> whose calls run the bound batch
    /// kernel request/response (arguments and results are marshaled to and from the sandbox). Throws if no
    /// kernel was registered for the contract.
    /// </summary>
    public TService RpcService<TService>() where TService : class
    {
        if (!_rpcServices.TryGetValue(typeof(TService), out var pluginId))
        {
            throw new InvalidOperationException(
                $"No kernel RPC service is registered for '{typeof(TService)}'. Call RegisterRpcServiceAsync first.");
        }

        return KernelRpcServiceProxy.Create<TService>(Kernels.Get(pluginId));
    }
}
