using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using ShaRPC.Core.Client;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core;

internal sealed class RpcPeerStreamingCalls
{
    private readonly ISerializer _serializer;

    public RpcPeerStreamingCalls(ISerializer serializer) => _serializer = serializer;

    public async Task<Stream> ReadStreamAsync(Task<ReceivedResponse> responseTask)
    {
        var received = await responseTask.ConfigureAwait(false);
        try
        {
            var receiver = TakeStreamReceiver(received, RpcStreamKind.Binary);
            return new RpcRemoteStream(receiver);
        }
        finally
        {
            received.Dispose();
        }
    }

    public async Task<Pipe> ReadPipeAsync(Task<ReceivedResponse> responseTask)
    {
        var received = await responseTask.ConfigureAwait(false);
        try
        {
            var receiver = TakeStreamReceiver(received, RpcStreamKind.Binary);
            return RpcPipeBridge.CreateReadablePipe(receiver, CancellationToken.None);
        }
        finally
        {
            received.Dispose();
        }
    }

    public async IAsyncEnumerable<T> EnumerateAsync<T>(
        Func<CancellationToken, Task<ReceivedResponse>> responseFactory,
        CancellationToken callCt,
        [EnumeratorCancellation] CancellationToken enumerationCt = default)
    {
        using var linked = LinkTokens(callCt, enumerationCt, out var ct);
        var received = await responseFactory(ct).ConfigureAwait(false);
        RpcStreamReceiver receiver;
        try
        {
            receiver = TakeStreamReceiver(received, RpcStreamKind.Items);
        }
        finally
        {
            received.Dispose();
        }

        var enumerable = new RpcRemoteAsyncEnumerable<T>(receiver, _serializer);
        await foreach (var item in enumerable.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public async Task<IAsyncEnumerable<T>> ReadAsyncEnumerableAsync<T>(
        Task<ReceivedResponse> responseTask)
    {
        var received = await responseTask.ConfigureAwait(false);
        try
        {
            var receiver = TakeStreamReceiver(received, RpcStreamKind.Items);
            return new RpcRemoteAsyncEnumerable<T>(receiver, _serializer);
        }
        finally
        {
            received.Dispose();
        }
    }

    private static CancellationTokenSource? LinkTokens(
        CancellationToken first,
        CancellationToken second,
        out CancellationToken linked)
    {
        if (first.CanBeCanceled && second.CanBeCanceled)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(first, second);
            linked = cts.Token;
            return cts;
        }

        linked = first.CanBeCanceled ? first : second;
        return null;
    }

    private static RpcStreamReceiver TakeStreamReceiver(
        ReceivedResponse received,
        RpcStreamKind expectedKind)
    {
        var handle = received.Response.Stream ??
            throw new ShaRpcProtocolException("Response did not open a stream.");
        if (handle.Kind != expectedKind || received.Stream is null)
        {
            throw new ShaRpcProtocolException($"Response stream kind was '{handle.Kind}', expected '{expectedKind}'.");
        }
        if (!received.Payload.IsEmpty)
        {
            throw new ShaRpcProtocolException("Streaming response payload must be empty.");
        }

        var stream = received.DetachStream() ??
            throw new ShaRpcProtocolException("Response stream receiver was already claimed.");
        if (received.DetachOutboundStreams() is { } outboundStreams)
        {
            stream.AttachOutboundStreams(outboundStreams);
        }

        return stream;
    }
}
