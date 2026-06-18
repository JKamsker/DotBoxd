using System.IO.Pipelines;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Streaming.Remote;

/// <summary>
/// Runtime implementation of <see cref="IRpcStreamingContext"/>.
/// </summary>
public sealed class RpcStreamingContext : IRpcStreamingContext
{
    private readonly RpcStreamManager? _streams;
    private readonly ISerializer? _serializer;
    private readonly CancellationToken _ct;
    private readonly RpcStreamHandle[]? _declaredInboundStreams;
    private int[]? _claimedInboundStreamIds;
    private RpcStreamAttachment? _response;
    private int _claimedInboundStreamCount;
    private bool _claimedSingleInboundStream;

    public static RpcStreamingContext Disabled { get; } = new();

    private RpcStreamingContext()
    {
    }

    internal RpcStreamingContext(
        RpcStreamManager streams,
        ISerializer serializer,
        CancellationToken ct,
        RpcStreamHandle[]? declaredInboundStreams = null)
    {
        _streams = streams;
        _serializer = serializer;
        _ct = ct;
        _declaredInboundStreams = NormalizeDeclaredInboundStreams(declaredInboundStreams);
    }

    internal RpcStreamAttachment? Response => _response;

    internal async ValueTask AbandonResponseAsync()
    {
        if (Interlocked.Exchange(ref _response, null) is not { } response)
        {
            return;
        }

        _streams?.ReleaseOutboundReservation(response.Handle.StreamId);
        await response.DisposeSourceBestEffortAsync("Streaming response cleanup failed").ConfigureAwait(false);
    }

    public Stream GetStream(RpcStreamHandle handle)
    {
        return new RpcRemoteStream(GetInbound(handle, RpcStreamKind.Binary));
    }

    public Pipe GetPipe(RpcStreamHandle handle)
    {
        return RpcPipeBridge.CreateReadablePipe(GetInbound(handle, RpcStreamKind.Binary), _ct);
    }

    public IAsyncEnumerable<T> GetAsyncEnumerable<T>(RpcStreamHandle handle)
    {
        return new RpcRemoteAsyncEnumerable<T>(
            GetInbound(handle, RpcStreamKind.Items),
            _serializer!);
    }

    public void SetResponse(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var handle = ReserveResponseHandle(RpcStreamKind.Binary);
        try
        {
            _response = RpcStreamAttachment.FromStream(handle, stream, leaveOpen: false);
        }
        catch
        {
            _streams!.RemoveOutbound(handle.StreamId);
            throw;
        }
    }

    public void SetResponse(Pipe pipe)
    {
        if (pipe is null)
        {
            throw new ArgumentNullException(nameof(pipe));
        }

        var handle = ReserveResponseHandle(RpcStreamKind.Binary);
        try
        {
            _response = RpcStreamAttachment.FromPipe(handle, pipe, completeReader: true);
        }
        catch
        {
            _streams!.RemoveOutbound(handle.StreamId);
            throw;
        }
    }

    public void SetResponse<T>(IAsyncEnumerable<T> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var handle = ReserveResponseHandle(RpcStreamKind.Items);
        try
        {
            _response = RpcStreamAttachment.FromAsyncEnumerable(handle, items);
        }
        catch
        {
            _streams!.RemoveOutbound(handle.StreamId);
            throw;
        }
    }

    private RpcStreamHandle ReserveResponseHandle(RpcStreamKind kind)
    {
        EnsureEnabled();
        if (_response is not null)
        {
            throw new InvalidOperationException("Only one streamed response can be set for an RPC call.");
        }

        return _streams!.ReserveOutbound(kind);
    }

    private RpcStreamReceiver GetInbound(RpcStreamHandle handle, RpcStreamKind expected)
    {
        EnsureEnabled();
        EnsureKind(handle, expected);
        ClaimDeclaredInbound(handle);
        return _streams!.GetRegisteredInbound(handle);
    }

    private void ClaimDeclaredInbound(RpcStreamHandle handle)
    {
        var declared = _declaredInboundStreams;
        if (declared is null ||
            !TryGetDeclaredKind(declared, handle.StreamId, out var declaredKind))
        {
            throw new ServiceProtocolException(
                $"Inbound stream id '{handle.StreamId}' was not declared by the request.");
        }

        if (declaredKind != handle.Kind)
        {
            throw new ServiceProtocolException(
                $"Inbound stream id '{handle.StreamId}' was declared as '{declaredKind}', not '{handle.Kind}'.");
        }

        if (declared.Length == 1)
        {
            if (_claimedSingleInboundStream)
            {
                throw new ServiceProtocolException(
                    $"Inbound stream id '{handle.StreamId}' was already claimed.");
            }

            _claimedSingleInboundStream = true;
            return;
        }

        var claimed = _claimedInboundStreamIds ??= new int[declared.Length];
        for (var i = 0; i < _claimedInboundStreamCount; i++)
        {
            if (claimed[i] == handle.StreamId)
            {
                throw new ServiceProtocolException(
                    $"Inbound stream id '{handle.StreamId}' was already claimed.");
            }
        }

        claimed[_claimedInboundStreamCount++] = handle.StreamId;
    }

    private static bool TryGetDeclaredKind(
        RpcStreamHandle[] declared,
        int streamId,
        out RpcStreamKind kind)
    {
        if (declared.Length == 1)
        {
            var handle = declared[0];
            kind = handle.Kind;
            return handle.StreamId == streamId;
        }

        foreach (var handle in declared)
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

    private void EnsureEnabled()
    {
        if (_streams is null)
        {
            throw new InvalidOperationException("This dispatch path does not support streaming.");
        }
    }

    private static void EnsureKind(RpcStreamHandle handle, RpcStreamKind expected)
    {
        if (handle.Kind != expected)
        {
            throw new ServiceProtocolException($"Stream '{handle.StreamId}' is '{handle.Kind}', not '{expected}'.");
        }
    }

    private static RpcStreamHandle[]? NormalizeDeclaredInboundStreams(
        RpcStreamHandle[]? handles)
    {
        if (handles is null || handles.Length == 0)
        {
            return null;
        }

        if (handles.Length == 1)
        {
            return handles;
        }

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

        return handles;
    }
}
