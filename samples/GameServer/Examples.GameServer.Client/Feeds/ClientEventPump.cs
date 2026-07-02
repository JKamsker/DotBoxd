using DotBoxD.Kernels.Game.Client.Abstractions.Events;
using DotBoxD.Kernels.Game.Client.Sandbox;

namespace DotBoxD.Kernels.Game.Client;

internal sealed class ClientEventPump
{
    private readonly object _gate = new();
    private readonly Queue<Action<ClientPluginHost>> _pending = new();
    private ClientPluginHost? _host;

    public void Bind(ClientPluginHost host)
    {
        lock (_gate)
        {
            _host = host;
            while (_pending.TryDequeue(out var publish))
            {
                publish(host);
            }
        }
    }

    public void OnMonsterKilled(MonsterKilledEvent e)
        => Publish(host => host.Publish(new ClientMonsterKilledEvent(e.MonsterId, e.KillerId, e.Level)));

    public void OnGoldChanged(GoldChangedEvent e)
        => Publish(host => host.Publish(new ClientGoldChangedEvent(e.EntityId, e.Balance, e.Delta)));

    public void OnAttackSeen(string targetId)
        => Publish(host => host.Publish(new ClientAttackSeenEvent(targetId)));

    private void Publish(Action<ClientPluginHost> publish)
    {
        lock (_gate)
        {
            if (_host is null)
            {
                _pending.Enqueue(publish);
                return;
            }

            publish(_host);
        }
    }
}
