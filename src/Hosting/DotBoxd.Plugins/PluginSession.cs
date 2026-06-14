namespace DotBoxd.Plugins;

using DotBoxd.Kernels;

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

    /// <summary>
    /// Installs a <b>kernel RPC service</b> package owned by this session (see
    /// <see cref="PluginServer.InstallRpcAsync"/>). Same ownership/atomicity guarantees as
    /// <see cref="InstallAsync"/>; the kernel is invoked request/response via
    /// <see cref="InstalledKernel.InvokeRpcAsync"/> and revoked when the session is disposed.
    /// </summary>
    public async ValueTask<InstalledKernel> InstallRpcAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var kernel = await _server.InstallOwnedRpcAsync(this, package, policy, cancellationToken).ConfigureAwait(false);
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

            kernel = _server.Kernels.Get(pluginId);
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
            return Volatile.Read(ref _disposed) == 0 && _owned.Contains(pluginId);
        }
        finally
        {
            _gate.Release();
        }
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
