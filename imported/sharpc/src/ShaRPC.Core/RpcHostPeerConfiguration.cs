namespace ShaRPC.Core;

internal sealed class RpcHostPeerConfiguration
{
    private readonly List<Action<RpcPeer>> _configure = new();
    private readonly object _lock = new();

    public void Add(Action<RpcPeer> configure)
    {
        lock (_lock)
        {
            _configure.Add(configure);
        }
    }

    public Action<RpcPeer>[] Snapshot()
    {
        lock (_lock)
        {
            return _configure.ToArray();
        }
    }
}
