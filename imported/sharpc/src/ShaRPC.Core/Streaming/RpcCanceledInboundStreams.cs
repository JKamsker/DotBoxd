using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;

namespace ShaRPC.Core.Streaming;

internal sealed class RpcCanceledInboundStreams
{
    internal const int Capacity = 1024;

    private readonly object _gate = new();
    private readonly LinkedList<int> _order = new();
    private readonly Dictionary<int, LinkedListNode<int>> _streamIds = new();
    private bool _overflowed;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _streamIds.Count;
            }
        }
    }

    internal int TrackingCount
    {
        get
        {
            lock (_gate)
            {
                return _order.Count;
            }
        }
    }

    public void Add(int streamId)
    {
        lock (_gate)
        {
            ThrowIfOverflowedLocked();
            if (_streamIds.ContainsKey(streamId))
            {
                return;
            }

            if (_streamIds.Count >= Capacity)
            {
                _overflowed = true;
                ThrowOverflow();
            }

            var node = _order.AddLast(streamId);
            _streamIds.Add(streamId, node);
        }
    }

    public void ThrowIfOverflowed()
    {
        lock (_gate)
        {
            ThrowIfOverflowedLocked();
        }
    }

    public bool TryConsumeItem(int streamId, Payload frame)
    {
        if (!Contains(streamId))
        {
            return false;
        }

        frame.Dispose();
        return true;
    }

    public bool TryRemove(int streamId)
    {
        lock (_gate)
        {
            return RemoveLocked(streamId);
        }
    }

    public bool Contains(int streamId)
    {
        lock (_gate)
        {
            return _streamIds.ContainsKey(streamId);
        }
    }

    public void Remove(int streamId)
    {
        lock (_gate)
        {
            RemoveLocked(streamId);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _streamIds.Clear();
            _order.Clear();
            _overflowed = false;
        }
    }

    private bool RemoveLocked(int streamId)
    {
        if (!_streamIds.Remove(streamId, out var node))
        {
            return false;
        }

        _order.Remove(node);
        if (_streamIds.Count < Capacity)
        {
            _overflowed = false;
        }

        return true;
    }

    private void ThrowIfOverflowedLocked()
    {
        if (_overflowed && _streamIds.Count >= Capacity)
        {
            ThrowOverflow();
        }

        _overflowed = false;
    }

    private static void ThrowOverflow() =>
        throw new ShaRpcProtocolException(
            "Canceled inbound stream tombstone capacity was exceeded.");
}
