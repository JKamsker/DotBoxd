using DotBoxD.Kernels.Game.Client.Abstractions.Events;
using DotBoxD.Kernels.Game.Client.Sandbox;

namespace DotBoxD.Kernels.Game.Client;

internal sealed class ClientEventPump
{
    private readonly object _gate = new();
    private readonly Queue<Func<ClientPluginHost, ValueTask>> _pending = new();
    private ClientPluginHost? _host;

    public async ValueTask BindAsync(ClientPluginHost host)
    {
        Func<ClientPluginHost, ValueTask>[] pending;
        lock (_gate)
        {
            _host = host;
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (var publish in pending)
        {
            await publish(host).ConfigureAwait(false);
        }
    }

    public ValueTask OnMonsterKilledAsync(MonsterKilledEvent e)
        => PublishAsync(host => host.PublishAsync(new ClientMonsterKilledEvent(e.MonsterId, e.KillerId, e.Level)));

    public ValueTask OnGoldChangedAsync(GoldChangedEvent e)
        => PublishAsync(host =>
        {
            host.Publish(new ClientGoldChangedEvent(e.EntityId, e.Balance, e.Delta));
            return ValueTask.CompletedTask;
        });

    public ValueTask OnAttackSeenAsync(string targetId)
        => PublishAsync(host =>
        {
            host.Publish(new ClientAttackSeenEvent(targetId));
            return ValueTask.CompletedTask;
        });

    private ValueTask PublishAsync(Func<ClientPluginHost, ValueTask> publish)
    {
        ClientPluginHost host;
        lock (_gate)
        {
            if (_host is null)
            {
                _pending.Enqueue(publish);
                return ValueTask.CompletedTask;
            }

            host = _host;
        }

        return publish(host);
    }
}
