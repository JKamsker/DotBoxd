using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins;

public sealed partial class PluginServer
{
    private async ValueTask<InstalledKernelPool> InstallPoolCoreAsync(
        PluginPackage package,
        int degreeOfParallelism,
        SandboxPolicy? policy,
        CancellationToken cancellationToken)
    {
        if (degreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(degreeOfParallelism),
                degreeOfParallelism,
                "kernel pool degree of parallelism must be positive.");
        }

        ThrowIfDisposed();
        PluginPackageValidator.Validate(package);
        var installPolicy = policy ?? _defaultPolicy;
        var (sandboxModule, sandboxPolicy) = PrepareSandboxInputs(package, installPolicy);
        var plan = await _host.PrepareAsync(
                sandboxModule,
                sandboxPolicy,
                cancellationToken)
            .ConfigureAwait(false);
        PluginPackageValidator.ValidatePrepared(package, plan, Events, installPolicy);
        var kernels = new InstalledKernel[degreeOfParallelism];
        for (var i = 0; i < kernels.Length; i++)
        {
            kernels[i] = new InstalledKernel(_host, plan, package, _executionMode);
        }

        var pool = new InstalledKernelPool(kernels);
        lock (_poolGate)
        {
            _kernelPools.Add(pool);
        }

        return pool;
    }

    private async ValueTask<InstalledKernel> InstallServerExtensionCoreAsync(
        PluginPackage package,
        SandboxPolicy? policy,
        object? owner,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        RpcKernelPackageValidator.Validate(package);
        var installPolicy = policy ?? _defaultPolicy;
        var (sandboxModule, sandboxPolicy) = PrepareSandboxInputs(package, installPolicy);
        var plan = await _host.PrepareAsync(sandboxModule, sandboxPolicy, cancellationToken)
            .ConfigureAwait(false);
        RpcKernelPackageValidator.ValidatePrepared(package, plan);
        var kernel = new InstalledKernel(_host, plan, package, _executionMode, owner);
        var replaced = AddKernel(kernel);
        if (replaced is not null)
        {
            RemoveKernelReferences(replaced);
        }

        ClearServerExtensionRegistrations(package.Manifest.PluginId);
        return kernel;
    }

    private async ValueTask<InstalledKernel> InstallCoreAsync(
        PluginPackage package,
        SandboxPolicy? policy,
        object? owner,
        CancellationToken cancellationToken,
        bool deferActivation = false)
    {
        ThrowIfDisposed();
        PluginPackageValidator.Validate(package);
        var installPolicy = policy ?? _defaultPolicy;
        var (sandboxModule, sandboxPolicy) = PrepareSandboxInputs(package, installPolicy);
        var plan = await _host.PrepareAsync(
                sandboxModule,
                sandboxPolicy,
                cancellationToken)
            .ConfigureAwait(false);
        PluginPackageValidator.ValidatePrepared(package, plan, Events, installPolicy);
        var kernel = new InstalledKernel(_host, plan, package, _executionMode, owner);
        if (deferActivation)
        {
            // Register as a non-current instance only; the caller wires it and then promotes it (which revokes the
            // incumbent). A wiring failure rolls this instance back with the incumbent untouched.
            Kernels.AddInstance(kernel);
            return kernel;
        }

        var replaced = AddKernel(kernel);
        if (replaced is not null)
        {
            RemoveKernelReferences(replaced);
        }

        return kernel;
    }

    private InstalledKernel? AddKernel(InstalledKernel kernel)
    {
        if (!LocalTerminalIdentity.IsLocalTerminal(kernel.Manifest))
        {
            return Kernels.Add(kernel);
        }

        Kernels.AddInstance(kernel);
        return null;
    }

    /// <summary>
    /// Installs an event kernel as a non-current <b>instance</b> so the caller can wire it before it displaces any
    /// same-id incumbent. Pair with <see cref="PromoteOwned"/> on wiring success or <see cref="UninstallOwned"/>
    /// on failure; until promoted, the incumbent (if any) stays current and un-revoked.
    /// </summary>
    internal ValueTask<InstalledKernel> InstallOwnedStagedAsync(
        PluginSession owner,
        PluginPackage package,
        SandboxPolicy? policy,
        CancellationToken cancellationToken)
        => InstallCoreAsync(package, policy, owner, cancellationToken, deferActivation: true);

    /// <summary>
    /// Promotes a staged install (see <see cref="InstallOwnedStagedAsync"/>) to current, revoking and detaching any
    /// prior same-id incumbent only now that wiring has succeeded. A local-terminal kernel is already a registered
    /// instance with no current-kernel semantics, so promotion is a no-op for it. No-op if the server is disposed.
    /// </summary>
    internal void PromoteOwned(PluginSession owner, InstalledKernel kernel)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (LocalTerminalIdentity.IsLocalTerminal(kernel.Manifest))
        {
            return;
        }

        var replaced = Kernels.Promote(kernel);
        if (replaced is not null)
        {
            RemoveKernelReferences(replaced);
        }
    }

}
