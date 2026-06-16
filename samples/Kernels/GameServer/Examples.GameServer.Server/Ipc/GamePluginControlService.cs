using DotBoxD.Kernels.Game.Server.Abstractions.Events;
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

        var package = PluginPackageJsonSerializer.Import(packageJson);
        Console.WriteLine($"[server] installing plugin kernel '{package.Manifest.PluginId}'...");
        var policy = ServerPolicy.ForKernel(_server.GetRequiredCapabilities(package));
        var kernel = await _session.InstallAsync(package, policy, ct).ConfigureAwait(false);
        WireHook(kernel);
        Console.WriteLine($"[server] installed plugin kernel '{kernel.Manifest.PluginId}'.");
        return kernel.Manifest.PluginId;
    }

    public async ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        var package = PluginPackageJsonSerializer.Import(packageJson);
        Console.WriteLine($"[server] installing subscription kernel '{package.Manifest.PluginId}'...");
        var policy = ServerPolicy.ForKernel(_server.GetRequiredCapabilities(package));
        var kernel = await _session.InstallAsync(package, policy, ct).ConfigureAwait(false);
        WireSubscription(kernel);
        Console.WriteLine($"[server] installed subscription kernel '{kernel.Manifest.PluginId}'.");
        return kernel.Manifest.PluginId;
    }

    public async ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        var package = PluginPackageJsonSerializer.Import(packageJson);
        Console.WriteLine($"[server] installing server extension '{package.Manifest.PluginId}'...");
        var policy = ServerPolicy.ForKernel(_server.GetRequiredCapabilities(package));
        var kernel = await _session.InstallServerExtensionAsync(package, policy, ct).ConfigureAwait(false);
        Console.WriteLine($"[server] installed server extension '{kernel.Manifest.PluginId}'.");
        return kernel.Manifest.PluginId;
    }

    public async ValueTask<byte[]> InvokeServerExtensionAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!_session.Owns(pluginId))
        {
            throw new InvalidOperationException($"Server extension '{pluginId}' is not owned by this plugin session.");
        }

        var kernel = _server.Kernels.Get(pluginId);
        var function = RpcEntrypoint(kernel);
        var rpcArguments = KernelRpcBinaryCodec.DecodeArguments(arguments);
        var liveSettings = kernel.Manifest.LiveSettings.Count;
        var callerCount = function.Parameters.Count - liveSettings;
        if (callerCount < 0 || rpcArguments.Length != callerCount)
        {
            throw new InvalidOperationException(
                $"Server extension '{pluginId}' expects {callerCount} argument(s) but received {rpcArguments.Length}.");
        }

        var sandboxArguments = new SandboxValue[rpcArguments.Length];
        for (var i = 0; i < rpcArguments.Length; i++)
        {
            sandboxArguments[i] = KernelRpcValueConverter.ToSandboxValue(rpcArguments[i], function.Parameters[i].Type);
        }

        var result = await kernel.InvokeServerExtensionAsync(sandboxArguments, ct).ConfigureAwait(false);
        return KernelRpcBinaryCodec.EncodeValue(KernelRpcValueConverter.FromSandboxValue(result));
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

    // The per-entity domain calls (KillMonster / IsMonster / GetEntity*) moved to GameWorldAccess, which
    // implements IGameWorldAccess directly. This control service is now control-plane only. (GetWorldAsync
    // stays because it returns a whole WorldSnapshot for the server's own diagnostics, not a domain read.)

    private void WireHook(InstalledKernel kernel)
    {
        // Map by the kernel's declared event so the server stays agnostic of plugin ids.
        var subscription = kernel.Manifest.Subscriptions.Count > 0
            ? kernel.Manifest.Subscriptions[0].Event
            : null;
        switch (subscription)
        {
            case "MonsterAggroEvent":
                _server.Hooks.On<MonsterAggroEvent>().Use(kernel);
                break;
            case "AttackEvent":
                _server.Hooks.On<AttackEvent>().Use(kernel);
                break;
            default:
                throw new InvalidOperationException(
                    $"Plugin '{kernel.Manifest.PluginId}' subscribes to unsupported event '{subscription}'.");
        }
    }

    private void WireSubscription(InstalledKernel kernel)
    {
        var subscription = kernel.Manifest.Subscriptions.Count > 0
            ? kernel.Manifest.Subscriptions[0].Event
            : null;
        switch (subscription)
        {
            case "MonsterAggroEvent":
                _server.Subscriptions.On<MonsterAggroEvent>().Use(kernel);
                break;
            case "AttackEvent":
                _server.Subscriptions.On<AttackEvent>().Use(kernel);
                break;
            default:
                throw new InvalidOperationException(
                    $"Plugin '{kernel.Manifest.PluginId}' subscribes to unsupported event '{subscription}'.");
        }
    }

    private static SandboxFunction RpcEntrypoint(InstalledKernel kernel)
    {
        if (kernel.Manifest.RpcEntrypoint is not { } entrypoint)
        {
            throw new InvalidOperationException($"Kernel '{kernel.Manifest.PluginId}' is not a server extension.");
        }

        foreach (var function in kernel.Package.Module.Functions)
        {
            if (function.IsEntrypoint && string.Equals(function.Id, entrypoint, StringComparison.Ordinal))
            {
                return function;
            }
        }

        throw new InvalidOperationException(
            $"Server extension '{kernel.Manifest.PluginId}' is missing entrypoint '{entrypoint}'.");
    }
}
