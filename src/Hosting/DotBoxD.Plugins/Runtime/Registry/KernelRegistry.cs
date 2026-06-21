using System.Collections;
using System.Diagnostics.CodeAnalysis;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins;

public sealed class KernelRegistry : IEnumerable<InstalledKernel>
{
    private readonly object _gate = new();
    private readonly Dictionary<string, InstalledKernel> _kernels = new(StringComparer.Ordinal);

    public InstalledKernel Get(string pluginId)
    {
        lock (_gate)
        {
            return _kernels[pluginId];
        }
    }

    public TypedInstalledKernel<TState> Get<TState>(string pluginId) where TState : class
        => new(Get(pluginId));

    /// <summary>
    /// Probes installation state without throwing, letting an admin/host UI discover whether a
    /// plugin id is currently installed and read its live kernel without catching
    /// <see cref="KeyNotFoundException"/>.
    /// </summary>
    public bool TryGet(string pluginId, [MaybeNullWhen(false)] out InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        lock (_gate)
        {
            return _kernels.TryGetValue(pluginId, out kernel);
        }
    }

    /// <summary>
    /// Returns a stable snapshot of the currently installed kernels for inventory rendering. The
    /// returned list is detached from registry internals, so it is safe to enumerate while installs
    /// and uninstalls continue concurrently.
    /// </summary>
    public IReadOnlyList<InstalledKernel> Snapshot()
    {
        lock (_gate)
        {
            return _kernels.Values.ToArray();
        }
    }

    /// <summary>
    /// Enumerates the currently installed kernels over a stable snapshot, so an admin/host UI can
    /// iterate the inventory directly (for example with <c>foreach</c> or LINQ) without taking a
    /// dependency on <see cref="Snapshot"/>. Enumeration is detached from registry internals and is
    /// therefore unaffected by concurrent installs and uninstalls.
    /// </summary>
    public IEnumerator<InstalledKernel> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal InstalledKernel GetByKernelType<TKernel>() where TKernel : class
    {
        var pluginId = KernelTypeMetadata.PluginId(typeof(TKernel));
        return Get(pluginId);
    }

    internal InstalledKernel? Add(InstalledKernel kernel)
    {
        InstalledKernel? revoke = null;
        lock (_gate)
        {
            if (_kernels.TryGetValue(kernel.Manifest.PluginId, out var existing) &&
                !ReferenceEquals(existing, kernel))
            {
                if (!ReferenceEquals(existing.OwnerId, kernel.OwnerId) &&
                    (existing.OwnerId is not null || kernel.OwnerId is not null))
                {
                    throw new SandboxValidationException([
                        new SandboxDiagnostic(
                            "DBXK060",
                            $"plugin id '{kernel.Manifest.PluginId}' is owned by another session and cannot be replaced.")
                    ]);
                }

                revoke = existing;
            }

            _kernels[kernel.Manifest.PluginId] = kernel;
        }

        revoke?.Revoke();
        return revoke;
    }

    /// <summary>
    /// Removes and revokes a kernel only if it is owned by <paramref name="owner"/> (or has no owner),
    /// so a session disposal never tears down another session's kernel that may have replaced this id.
    /// </summary>
    internal InstalledKernel? RemoveOwned(PluginSession owner, string pluginId)
    {
        InstalledKernel? kernel;
        lock (_gate)
        {
            if (!_kernels.TryGetValue(pluginId, out kernel))
            {
                return null;
            }

            if (kernel.OwnerId is not null && !ReferenceEquals(kernel.OwnerId, owner))
            {
                return null;
            }

            _kernels.Remove(pluginId);
        }

        kernel.Revoke();
        return kernel;
    }

    internal InstalledKernel? Remove(string pluginId)
    {
        InstalledKernel? kernel;
        lock (_gate)
        {
            if (!_kernels.Remove(pluginId, out kernel))
            {
                return null;
            }
        }

        if (kernel is null)
        {
            return null;
        }

        kernel.Revoke();
        return kernel;
    }

    internal InstalledKernel? Remove(InstalledKernel kernel)
    {
        lock (_gate)
        {
            if (!_kernels.TryGetValue(kernel.Manifest.PluginId, out var current) ||
                !ReferenceEquals(current, kernel))
            {
                return null;
            }

            _kernels.Remove(kernel.Manifest.PluginId);
        }

        kernel.Revoke();
        return kernel;
    }
}
