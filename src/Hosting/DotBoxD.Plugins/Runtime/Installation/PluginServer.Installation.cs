using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Rpc;
using DotBoxD.Plugins.Runtime.Validation;

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
        var plan = await _host.PrepareAsync(
                package.Module,
                PreparePolicyForModule(package, installPolicy),
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
        var plan = await _host.PrepareAsync(package.Module, policy ?? _defaultPolicy, cancellationToken)
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

    private InstalledKernel? AddKernel(InstalledKernel kernel)
    {
        if (!LocalTerminalIdentity.IsLocalTerminal(kernel.Manifest))
        {
            return Kernels.Add(kernel);
        }

        Kernels.AddInstance(kernel);
        return null;
    }

    private SandboxPolicy PreparePolicyForModule(PluginPackage package, SandboxPolicy installPolicy)
    {
        var moduleRequired = new HashSet<string>(_host.GetRequiredCapabilities(package.Module), StringComparer.Ordinal);
        var pluginRequests = new HashSet<string>(
            package.Module.CapabilityRequests.Select(request => request.Id),
            StringComparer.Ordinal);
        var manifestOnly = PluginRequiredCapabilityMetadata.Read(package.Module)
            .Where(capability => !moduleRequired.Contains(capability) && !pluginRequests.Contains(capability))
            .ToArray();
        if (manifestOnly.Length == 0)
        {
            return installPolicy;
        }

        var grants = installPolicy.Grants
            .Where(grant => !MatchesAny(grant.Id, manifestOnly))
            .ToArray();
        return grants.Length == installPolicy.Grants.Count
            ? installPolicy
            : installPolicy with { Grants = grants };
    }

    private static bool MatchesAny(string grantId, IReadOnlyList<string> capabilities)
    {
        for (var i = 0; i < capabilities.Count; i++)
        {
            if (CapabilityPattern.Matches(grantId, capabilities[i]))
            {
                return true;
            }
        }

        return false;
    }
}
