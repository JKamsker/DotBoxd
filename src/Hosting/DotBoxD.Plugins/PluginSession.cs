using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;

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
    private readonly HashSet<string> _ownedInstallIds = new(StringComparer.Ordinal);
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
            _ownedInstallIds.Add(kernel.InstallId);
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
            _ownedInstallIds.Add(kernel.InstallId);
            return kernel;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Installs an event-kernel <paramref name="package"/> owned by this session and wires it in one step,
    /// rolling the install back if validation or wiring fails — the install ceremony every host used to
    /// hand-write. <paramref name="validate"/> runs <b>before</b> install for host route checks;
    /// <paramref name="policy"/> overrides the default install policy; <paramref name="wire"/> is the host's
    /// routing choice (typically <see cref="PluginServer.WireHook"/> or
    /// <see cref="PluginServer.WireSubscription"/> with host wire options). On any failure the just-installed
    /// kernel is uninstalled by its exact install id — so a same-id incumbent is never disturbed — and the
    /// original exception is rethrown.
    /// </summary>
    /// <remarks>Opt-in convenience: hand-write the equivalent with <see cref="InstallAsync"/> + your wire
    /// action + <see cref="Uninstall"/> on failure if this shape doesn't fit — all public.</remarks>
    public async ValueTask<InstalledKernel> InstallAndWireAsync(
        PluginPackage package,
        Action<InstalledKernel> wire,
        Func<PluginPackage, SandboxPolicy>? policy = null,
        Action<PluginPackage>? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(wire);

        validate?.Invoke(package);

        InstalledKernel? kernel = null;
        try
        {
            kernel = await InstallAsync(package, policy?.Invoke(package), cancellationToken).ConfigureAwait(false);
            wire(kernel);
            return kernel;
        }
        catch
        {
            if (kernel is not null)
            {
                RollBack(kernel);
            }

            throw;
        }
    }

    private void RollBack(InstalledKernel kernel)
    {
        try
        {
            _gate.Wait();
            try
            {
                _ownedInstallIds.Remove(kernel.InstallId);
            }
            finally
            {
                _gate.Release();
            }

            _server.UninstallOwned(this, kernel.InstallId);
        }
        catch
        {
            // Best-effort rollback: the original install/wire failure is the actionable error and is rethrown.
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

    public bool Uninstall(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        string installId;
        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
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
