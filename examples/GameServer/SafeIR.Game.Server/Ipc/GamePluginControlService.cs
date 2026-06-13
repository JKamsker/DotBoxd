namespace SafeIR.Game.Server;

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
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public GamePluginControlService(PluginServer server, PluginSession session, GameCommandSink sink, GameWorld world)
    {
        _server = server;
        _session = session;
        _sink = sink;
        _world = world;
    }

    /// <summary>Completes once the plugin has installed its kernels and is holding the connection.</summary>
    public Task Ready => _ready.Task;

    /// <summary>Releases the plugin's <see cref="HoldUntilShutdownAsync"/> so it can disconnect.</summary>
    public void SignalShutdown() => _shutdown.TrySetResult();

    public async ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        // Grant each kernel exactly what its analyzer-derived manifest declares it needs (least
        // privilege). The plugin cannot widen this: RequiredCapabilities reflects what the verified IR
        // actually touches, not what the plugin asserts.
        var package = PluginPackageJsonSerializer.Import(packageJson);
        var policy = ServerPolicy.ForKernel(package.Manifest.RequiredCapabilities);
        var kernel = await _session.InstallAsync(package, policy, ct).ConfigureAwait(false);
        WireHook(kernel);
        return kernel.Manifest.PluginId;
    }

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
        _ready.TrySetResult();
        await _shutdown.Task.ConfigureAwait(false);
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

    private void WireHook(InstalledKernel kernel)
    {
        // Map by the kernel's declared subscription event so the server stays agnostic of plugin ids.
        var subscription = kernel.Manifest.Subscriptions.Count > 0
            ? kernel.Manifest.Subscriptions[0].Event
            : null;
        switch (subscription)
        {
            case "MonsterAggroEvent":
                _server.Hooks.On<MonsterAggroEvent>().UseKernel(kernel);
                break;
            case "AttackEvent":
                _server.Hooks.On<AttackEvent>().UseKernel(kernel);
                break;
            default:
                throw new InvalidOperationException(
                    $"Plugin '{kernel.Manifest.PluginId}' subscribes to unsupported event '{subscription}'.");
        }
    }
}
