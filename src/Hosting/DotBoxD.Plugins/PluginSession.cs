using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins;

using DotBoxD.Kernels;

/// <summary>
/// Server-side owner of the kernels installed over one connection. Each kernel it installs is tagged
/// with this session as its owner, so another session cannot replace or mutate it. Disposing the
/// session — e.g. from a transport disconnect handler — revokes and unregisters every kernel it owns.
/// </summary>
public sealed class PluginSession : IDisposable, IAsyncDisposable
{
    private readonly PluginServer _server;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _owned = new(StringComparer.Ordinal);
    private int _disposed;

    internal PluginSession(PluginServer server) => _server = server;

    /// <summary>
    /// Installs a package owned by this session. The install and the ownership bookkeeping are atomic
    /// with respect to <see cref="Dispose"/>, so a kernel can never be installed-then-orphaned by a
    /// concurrent disconnect.
    /// </summary>
    public async ValueTask<InstalledKernel> InstallAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var kernel = await _server.InstallOwnedAsync(this, package, policy, cancellationToken).ConfigureAwait(false);
            _owned.Add(package.Manifest.PluginId);
            return kernel;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<InstalledKernel> InstallLocalCallbackAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var kernel = await _server.InstallOwnedLocalCallbackAsync(this, package, policy, cancellationToken)
                .ConfigureAwait(false);
            _owned.Add(package.Manifest.PluginId);
            return kernel;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Installs a <b>server extension</b> package owned by this session (see
    /// <see cref="PluginServer.InstallServerExtensionAsync"/>). Same ownership/atomicity guarantees as
    /// <see cref="InstallAsync"/>; the kernel is invoked request/response via
    /// <see cref="InstalledKernel.InvokeServerExtensionAsync"/> and revoked when the session is disposed.
    /// </summary>
    public async ValueTask<InstalledKernel> InstallServerExtensionAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var kernel = await _server.InstallOwnedServerExtensionAsync(this, package, policy, cancellationToken).ConfigureAwait(false);
            _owned.Add(package.Manifest.PluginId);
            return kernel;
        }
        finally
        {
            _gate.Release();
        }
    }

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
            if (!_owned.Contains(pluginId))
            {
                throw new SandboxValidationException([
                    new SandboxDiagnostic(
                        "DBXK061",
                        $"plugin id '{pluginId}' is not owned by this session.")
                ]);
            }

            if (!_server.Kernels.TryGet(pluginId, out var ownedKernel) ||
                !ReferenceEquals(ownedKernel.OwnerId, this))
            {
                _owned.Remove(pluginId);
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
            if (Volatile.Read(ref _disposed) != 0 || !_owned.Contains(pluginId))
            {
                return false;
            }

            if (_server.Kernels.TryGet(pluginId, out var kernel) &&
                ReferenceEquals(kernel.OwnerId, this))
            {
                return true;
            }

            _owned.Remove(pluginId);
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
        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!_owned.Remove(pluginId))
            {
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }

        return _server.UninstallOwned(this, pluginId);
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
            owned = [.. _owned];
            _owned.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var pluginId in owned)
        {
            _server.UninstallOwned(this, pluginId);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
