using System.Diagnostics.CodeAnalysis;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins;

/// <summary>
/// Server-side owner of the kernels installed over one connection. Each kernel it installs is tagged
/// with this session as its owner, so another session cannot replace or mutate it. Disposing the
/// session — e.g. from a transport disconnect handler — revokes and unregisters every kernel it owns.
/// </summary>
/// <remarks>The install/stage/promote surface lives in the <c>PluginSession.Install.cs</c> partial.</remarks>
public sealed partial class PluginSession : IDisposable, IAsyncDisposable
{
    private readonly PluginServer _server;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _ownedInstallIds = new(StringComparer.Ordinal);
    private int _disposed;

    internal PluginSession(PluginServer server) => _server = server;

    /// <summary>
    /// Updates live settings for a kernel this session owns. Rejects ids the session does not own so
    /// one plugin cannot tune another plugin's kernel.
    /// </summary>
    public async ValueTask UpdateSettingsAsync(
        string pluginId,
        IReadOnlyDictionary<string, object?> values,
        bool atomic = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(values);

        InstalledKernel kernel;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _server.ThrowIfDisposed();
            if (!_server.Kernels.TryGet(pluginId, out var ownedKernel) ||
                !ReferenceEquals(ownedKernel.OwnerId, this) ||
                !_ownedInstallIds.Contains(ownedKernel.InstallId))
            {
                throw new SandboxValidationException([
                    new SandboxDiagnostic(
                        "DBXK061",
                        $"plugin id '{pluginId}' is not owned by this session.")
                ]);
            }

            kernel = ownedKernel;
        }
        finally
        {
            _gate.Release();
        }

        await kernel.ModifySettingsAsync(values, atomic, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether this (non-disposed) session currently owns <paramref name="pluginId"/>.</summary>
    public bool Owns(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        _gate.Wait();
        try
        {
            _server.ThrowIfDisposed();
            if (Volatile.Read(ref _disposed) != 0 ||
                !_server.Kernels.TryGet(pluginId, out var kernel) ||
                !ReferenceEquals(kernel.OwnerId, this))
            {
                return false;
            }

            return _ownedInstallIds.Contains(kernel.InstallId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Atomically fetches the kernel currently registered under <paramref name="pluginId"/> AND verifies this
    /// session owns it, returning that exact instance. Use this instead of a separate <see cref="Owns"/> + registry
    /// lookup so authorization binds to the kernel actually returned — there is no window in which a same-id
    /// hot-replace could swap a different kernel in between the check and the fetch.
    /// </summary>
    public bool TryGetOwned(string pluginId, [MaybeNullWhen(false)] out InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        _gate.Wait();
        try
        {
            _server.ThrowIfDisposed();
            if (Volatile.Read(ref _disposed) == 0 &&
                _server.Kernels.TryGet(pluginId, out var owned) &&
                ReferenceEquals(owned.OwnerId, this) &&
                _ownedInstallIds.Contains(owned.InstallId))
            {
                kernel = owned;
                return true;
            }

            kernel = null;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool Uninstall(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        string installId;
        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _server.ThrowIfDisposed();
            if (!_server.Kernels.TryGet(pluginId, out var kernel) ||
                !ReferenceEquals(kernel.OwnerId, this) ||
                !_ownedInstallIds.Remove(kernel.InstallId))
            {
                return false;
            }

            installId = kernel.InstallId;
        }
        finally
        {
            _gate.Release();
        }

        return _server.UninstallOwned(this, installId);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Wait for any in-flight install to finish (it holds the gate) so the just-installed kernel is
        // captured here and not orphaned, then revoke + unregister everything this session owns.
        string[] owned;
        _gate.Wait();
        try
        {
            owned = [.. _ownedInstallIds];
            _ownedInstallIds.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var installId in owned)
        {
            _server.UninstallOwned(this, installId);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
