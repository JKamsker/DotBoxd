using DotBoxD.Services.Attributes;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc;

/// <summary>
/// FRAMEWORK control-plane only. After unifying the domain surface onto <c>IGameWorldAccess</c>, the
/// per-entity domain calls (KillMonster / IsMonster / GetEntity*) live on <c>IGameWorldAccess</c> — the
/// server implements them there and the plugin RPC-proxies them. What remains here is the IR-shipping +
/// lifecycle plumbing that backs <c>Replace</c>/<c>Extend</c>/<c>Get</c>/<c>InvokeAsync</c> and the
/// <c>IPluginServer&lt;TWorld&gt;</c> lifecycle — the dev never calls these directly.
/// </summary>
[DotBoxDService]
public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
{
    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);

    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);

    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);

    ValueTask UpdateSettingsAsync(
        string pluginId,
        LiveSettingUpdate[] updates,
        bool atomic = false,
        CancellationToken ct = default);

    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);

    ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default);

    ValueTask<string[]> DrainEffectsAsync(CancellationToken ct = default);
}
