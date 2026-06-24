using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Kernels.Game.Server.Simulation;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// Implements the IPC control plane for one plugin connection over the running <see cref="PluginServer"/>
/// and <see cref="GameWorld"/>. The plugin installs untrusted kernels as opaque verified IR through its
/// owning <see cref="PluginSession"/> — the server never sees kernel source — and the service wires the
/// hook for whichever event the installed kernel subscribes to. The plugin then holds the connection
/// (<see cref="HoldUntilShutdownAsync"/>) until the server's with-plugin phase finishes.
/// </summary>
internal sealed class GamePluginControlService : IGamePluginControlService
{
    private readonly PluginServer _server;
    private readonly PluginSession _session;
    private readonly GameCommandSink _sink;
    private readonly GameWorld _world;
    private readonly GamePluginKernelWiring _kernelWiring;
    private readonly GamePluginServerExtensionInvoker _serverExtensions;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Back-compat 4-arg ctor (no event-callback transport): a plugin that never uses a remote RunLocal chain
    // needs no callback. Kept as a distinct overload so reflection-based construction with four positional
    // arguments resolves unambiguously.
    public GamePluginControlService(PluginServer server, PluginSession session, GameCommandSink sink, GameWorld world)
        : this(server, session, sink, world, null)
    {
    }

    public GamePluginControlService(
        PluginServer server,
        PluginSession session,
        GameCommandSink sink,
        GameWorld world,
        IPluginEventCallback? eventCallback)
        : this(
            server,
            session,
            sink,
            world,
            new GamePluginKernelWiring(server, world, eventCallback),
            new GamePluginServerExtensionInvoker(server, session))
    {
    }

    private GamePluginControlService(
        PluginServer server,
        PluginSession session,
        GameCommandSink sink,
        GameWorld world,
        GamePluginKernelWiring kernelWiring,
        GamePluginServerExtensionInvoker serverExtensions)
    {
        _server = server;
        _session = session;
        _sink = sink;
        _world = world;
        _kernelWiring = kernelWiring;
        _serverExtensions = serverExtensions;
    }

    /// <summary>Completes once the plugin has installed its kernels and is holding the connection.</summary>
    public Task Ready => _ready.Task;

    /// <summary>Releases the plugin's <see cref="HoldUntilShutdownAsync"/> so it can disconnect.</summary>
    public void SignalShutdown() => _shutdown.TrySetResult();

    public async ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        var package = PluginPackageJsonSerializer.Import(packageJson);
        _kernelWiring.ValidateRoute(package);
        Console.WriteLine($"[server] installing plugin kernel '{package.Manifest.PluginId}'...");
        var kernel = await InstallAndWireAsync(package, _kernelWiring.WireHook, ct).ConfigureAwait(false);
        Console.WriteLine($"[server] installed plugin kernel '{kernel.Manifest.PluginId}'.");
        return InstallRouteId(kernel);
    }

    public async ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        var package = PluginPackageJsonSerializer.Import(packageJson);
        _kernelWiring.ValidateRoute(package);
        Console.WriteLine($"[server] installing subscription kernel '{package.Manifest.PluginId}'...");
        var kernel = await InstallAndWireAsync(package, _kernelWiring.WireSubscription, ct).ConfigureAwait(false);
        EventIndexDiagnostics.Report(kernel);
        Console.WriteLine($"[server] installed subscription kernel '{kernel.Manifest.PluginId}'.");
        return InstallRouteId(kernel);
    }

    public async ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        var package = PluginPackageJsonSerializer.Import(packageJson);
        Console.WriteLine($"[server] installing server extension '{package.Manifest.PluginId}'...");
        var policy = ServerPolicy.ForKernel(_server.GetRequiredCapabilities(package));
        try
        {
            var kernel = await _session.InstallServerExtensionAsync(package, policy, ct).ConfigureAwait(false);
            Console.WriteLine($"[server] installed server extension '{kernel.Manifest.PluginId}'.");
            return kernel.Manifest.PluginId;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[server] server extension install failed: {ex}");
            throw;
        }
    }

    public ValueTask<byte[]> InvokeServerExtensionAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken ct = default)
        => _serverExtensions.InvokeAsync(pluginId, arguments, ct);

    public ValueTask UpdateSettingsAsync(
        string pluginId,
        LiveSettingUpdate[] updates,
        bool atomic = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(updates);
        var values = new Dictionary<string, object?>(updates.Length, StringComparer.Ordinal);
        foreach (var update in updates)
        {
            values[update.Name] = update.Value;
        }

        // Owner-checked: the session rejects ids it does not own.
        return _session.UpdateSettingsAsync(pluginId, values, atomic, ct);
    }

    public async ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ready.TrySetResult();
        await _shutdown.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_world.Snapshot());
    }

    public ValueTask<string[]> DrainEffectsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_sink.DrainEffects());
    }

    private static string InstallRouteId(InstalledKernel kernel)
        => kernel.CallbackSubscriptionId ?? kernel.Manifest.PluginId;

    private async ValueTask<InstalledKernel> InstallAndWireAsync(
        PluginPackage package,
        Action<InstalledKernel> wire,
        CancellationToken ct)
    {
        var policy = ServerPolicy.ForKernel(_server.GetRequiredCapabilities(package));
        InstalledKernel? kernel = null;
        try
        {
            kernel = await _session.InstallAsync(package, policy, ct).ConfigureAwait(false);
            wire(kernel);
            return kernel;
        }
        catch
        {
            if (kernel is not null)
            {
                RollBackInstalledKernel(kernel);
            }

            throw;
        }
    }

    private void RollBackInstalledKernel(InstalledKernel kernel)
    {
        try
        {
            _session.Uninstall(kernel.Manifest.PluginId);
        }
        catch (Exception rollbackError)
        {
            Console.Error.WriteLine(
                $"[server] rollback failed for plugin kernel '{kernel.Manifest.PluginId}': {rollbackError}");
        }
    }

    // The per-entity domain calls (KillMonster / IsMonster / GetEntity*) moved to GameWorldAccess, which
    // implements IGameWorldAccess directly. This control service is now control-plane only. (GetWorldAsync
    // stays because it returns a whole WorldSnapshot for the server's own diagnostics, not a domain read.)
}
