using System.Text.Json;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Kernels.Game.Server.Simulation;

namespace DotBoxD.Kernels.Game.Server.Ipc;

internal static class GameClientFeedForwarder
{
    public static void Attach(GameWorld world, IPluginEventCallback callback)
    {
        world.Subscriptions.On<MonsterKilledEvent>()
            .RunLocal(e => callback.OnEventAsync(
                GameClientFeedIds.MonsterKilled,
                JsonSerializer.SerializeToUtf8Bytes(e)));
        world.Subscriptions.On<GoldChangedEvent>()
            .Where(e => e.EntityId == "player-1")
            .RunLocal(e => callback.OnEventAsync(
                GameClientFeedIds.GoldChanged,
                JsonSerializer.SerializeToUtf8Bytes(e)));
        world.Subscriptions.On<AttackEvent>()
            .Where(e => e.Damage >= 5)
            .RunLocal(e => callback.OnEventAsync(
                GameClientFeedIds.AttackSeen,
                JsonSerializer.SerializeToUtf8Bytes(e)));
    }
}
