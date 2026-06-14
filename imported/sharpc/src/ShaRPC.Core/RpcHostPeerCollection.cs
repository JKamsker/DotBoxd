using System.Collections.Concurrent;

namespace ShaRPC.Core;

internal sealed class RpcHostPeerCollection
{
    private readonly ConcurrentDictionary<RpcPeer, byte> _peers = new();
    private readonly ConcurrentDictionary<Task, byte> _cleanupTasks = new();

    public void Add(RpcPeer peer) => _peers.TryAdd(peer, 0);

    public void Remove(RpcPeer peer) => _peers.TryRemove(peer, out _);

    public void DisposeInBackground(RpcPeer peer)
    {
        var cleanupTask = Task.Run(async () =>
        {
            try
            {
                await peer.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup.
            }
        });
        _cleanupTasks.TryAdd(cleanupTask, 0);
        _ = cleanupTask.ContinueWith(
            static (task, state) =>
                ((ConcurrentDictionary<Task, byte>)state!).TryRemove(task, out _),
            _cleanupTasks,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    public async Task CloseAllAsync()
    {
        // A peer that disconnects naturally just before this runs may be disposed twice:
        // once by DisposeInBackground and once here. RpcPeer.DisposeAsync is idempotent.
        var tasks = _peers.Keys.Select(peer => DisposeOnePeerAsync(peer)).ToArray();
        if (tasks.Length != 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        _peers.Clear();
    }

    private static async Task DisposeOnePeerAsync(RpcPeer peer)
    {
        try
        {
            await peer.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    public async Task AwaitCleanupAsync()
    {
        var tasks = _cleanupTasks.Keys.ToArray();
        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Peer cleanup is best-effort and each task observes its own dispose failures.
        }
    }
}
