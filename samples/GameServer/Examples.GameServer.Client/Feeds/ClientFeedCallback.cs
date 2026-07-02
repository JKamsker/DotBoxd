using System.Text.Json;

namespace DotBoxD.Kernels.Game.Client;

internal sealed class ClientFeedCallback(ClientEventPump pump) : IPluginEventCallback
{
    public ValueTask OnEventAsync(
        string subscriptionId,
        ReadOnlyMemory<byte> projectedValue,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        switch (subscriptionId)
        {
            case GameClientFeedIds.MonsterKilled:
                pump.OnMonsterKilled(Read<MonsterKilledEvent>(projectedValue));
                break;
            case GameClientFeedIds.GoldChanged:
                pump.OnGoldChanged(Read<GoldChangedEvent>(projectedValue));
                break;
            case GameClientFeedIds.AttackSeen:
                pump.OnAttackSeen(Read<AttackEvent>(projectedValue).TargetId);
                break;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]> OnResultAsync(
        string subscriptionId,
        ReadOnlyMemory<byte> contextValue,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Array.Empty<byte>());
    }

    private static T Read<T>(ReadOnlyMemory<byte> payload)
        => JsonSerializer.Deserialize<T>(payload.Span)
           ?? throw new InvalidOperationException($"Client feed payload for {typeof(T).Name} was empty.");
}
