using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Streaming.Remote;

internal sealed class RpcInboundStreamClaims
{
    private readonly RpcStreamHandle[] _declared;
    private int[]? _claimedStreamIds;
    private int _claimedCount;
    private bool _claimedSingle;

    private RpcInboundStreamClaims(RpcStreamHandle[] declared)
    {
        _declared = declared;
    }

    public static RpcInboundStreamClaims? Create(RpcStreamHandle[]? handles)
    {
        if (handles is null || handles.Length == 0)
        {
            return null;
        }

        if (handles.Length > 1)
        {
            EnsureNoDuplicateIds(handles);
        }

        return new RpcInboundStreamClaims(handles);
    }

    public void Claim(RpcStreamHandle handle)
    {
        if (!TryGetDeclaredKind(handle.StreamId, out var declaredKind))
        {
            throw new ServiceProtocolException(
                $"Inbound stream id '{handle.StreamId}' was not declared by the request.");
        }

        if (declaredKind != handle.Kind)
        {
            throw new ServiceProtocolException(
                $"Inbound stream id '{handle.StreamId}' was declared as '{declaredKind}', not '{handle.Kind}'.");
        }

        if (_declared.Length == 1)
        {
            if (_claimedSingle)
            {
                ThrowAlreadyClaimed(handle.StreamId);
            }

            _claimedSingle = true;
            return;
        }

        if (IsClaimed(handle.StreamId))
        {
            ThrowAlreadyClaimed(handle.StreamId);
        }

        var claimed = _claimedStreamIds ??= new int[_declared.Length];
        claimed[_claimedCount++] = handle.StreamId;
    }

    public void EnsureAllClaimed()
    {
        if (_declared.Length == 1)
        {
            if (!_claimedSingle)
            {
                ThrowUnclaimed(_declared[0].StreamId);
            }

            return;
        }

        if (_claimedCount == _declared.Length)
        {
            return;
        }

        foreach (var handle in _declared)
        {
            if (!IsClaimed(handle.StreamId))
            {
                ThrowUnclaimed(handle.StreamId);
            }
        }
    }

    private bool TryGetDeclaredKind(int streamId, out RpcStreamKind kind)
    {
        foreach (var handle in _declared)
        {
            if (handle.StreamId == streamId)
            {
                kind = handle.Kind;
                return true;
            }
        }

        kind = default;
        return false;
    }

    private bool IsClaimed(int streamId)
    {
        var claimed = _claimedStreamIds;
        if (claimed is null)
        {
            return false;
        }

        for (var i = 0; i < _claimedCount; i++)
        {
            if (claimed[i] == streamId)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureNoDuplicateIds(RpcStreamHandle[] handles)
    {
        for (var i = 0; i < handles.Length - 1; i++)
        {
            var streamId = handles[i].StreamId;
            for (var j = i + 1; j < handles.Length; j++)
            {
                if (handles[j].StreamId == streamId)
                {
                    throw new ArgumentException(
                        $"Duplicate inbound stream id '{streamId}'.",
                        nameof(handles));
                }
            }
        }
    }

    private static void ThrowAlreadyClaimed(int streamId)
    {
        throw new ServiceProtocolException($"Inbound stream id '{streamId}' was already claimed.");
    }

    private static void ThrowUnclaimed(int streamId)
    {
        throw new ServiceProtocolException(
            $"Inbound stream id '{streamId}' was declared by the request but was not claimed.");
    }
}
