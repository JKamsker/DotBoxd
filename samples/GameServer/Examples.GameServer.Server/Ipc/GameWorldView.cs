using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Kernels.Game.Server.Simulation;

namespace DotBoxD.Kernels.Game.Server.Ipc;

internal sealed class GameWorldView : IGameWorldView
{
    private readonly GameWorld _world;

    public GameWorldView(GameWorld world) => _world = world;

    public ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_world.Snapshot());
    }

    public ValueTask<int> GetBalanceAsync(string entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_world.Economy.GetBalance(entityId));
    }
}
