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
    private readonly RpcInboundStreamClaims? _inboundClaims;
    private RpcStreamAttachment? _response;

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
        _inboundClaims = RpcInboundStreamClaims.Create(declaredInboundStreams);
    }

    internal RpcStreamAttachment? Response => _response;

    internal void EnsureAllDeclaredInboundStreamsClaimed()
    {
        _inboundClaims?.EnsureAllClaimed();
    }

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
        var claims = _inboundClaims;
        if (claims is null)
        {
            throw new ServiceProtocolException(
                $"Inbound stream id '{handle.StreamId}' was not declared by the request.");
        }

        claims.Claim(handle);
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

}
