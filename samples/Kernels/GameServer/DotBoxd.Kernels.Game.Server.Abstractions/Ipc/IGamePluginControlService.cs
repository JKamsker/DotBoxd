namespace DotBoxd.Kernels.Game.Server.Abstractions;

using DotBoxd.Services.Attributes;

/// <summary>
/// Control plane the plugin host calls over IPC. The host ships opaque verified IR
/// (<see cref="InstallPluginAsync"/>), tunes live settings (<see cref="UpdateSettingsAsync"/>), and
/// can observe the running simulation (<see cref="GetWorldAsync"/>, <see cref="DrainEffectsAsync"/>).
/// </summary>
[DotBoxdService]
public interface IGamePluginControlService
{
    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);

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
}
