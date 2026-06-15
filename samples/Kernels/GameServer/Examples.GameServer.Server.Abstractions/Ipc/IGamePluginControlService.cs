using DotBoxD.Services.Attributes;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc;

/// <summary>
/// Control plane the plugin host calls over IPC. The host ships opaque verified IR
/// (<see cref="InstallPluginAsync"/> and <see cref="InstallServerExtensionAsync"/>), tunes live settings,
/// invokes plugin-owned server extensions, and can call ordinary server APIs such as
/// <see cref="KillMonsterAsync"/>.
/// </summary>
[DotBoxDService]
public interface IGamePluginControlService : IServerExtensionWireClient
{
    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);

    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);

    ValueTask UpdateSettingsAsync(
        string pluginId,
        LiveSettingUpdate[] updates,
        bool atomic = false,
        CancellationToken ct = default);

    /// <summary>
    /// Called by the plugin after it has installed its kernels. It holds the connection open — keeping
    /// the kernels owned and live — until the server finishes its with-plugin phase. When it returns,
    /// the plugin disconnects, and ownership unloads its kernels.
    /// </summary>
    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);

    ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default);

    ValueTask<string[]> DrainEffectsAsync(CancellationToken ct = default);

    ValueTask<bool> KillMonsterAsync(string monsterId, CancellationToken ct = default);

    ValueTask<bool> IsMonsterAsync(string entityId, CancellationToken ct = default);

    ValueTask<int> GetEntityHealthAsync(string entityId, CancellationToken ct = default);

    ValueTask<int> GetEntityLevelAsync(string entityId, CancellationToken ct = default);

    ValueTask<int> GetEntityPositionAsync(string entityId, CancellationToken ct = default);
}
