using System.Runtime.CompilerServices;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Tests.Streaming.Core;

internal static class StreamingProtocolRegressionTestSupport
{
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    public static RpcPeerInboundDispatcher CreateInbound(
        ISerializer serializer,
        RpcStreamManager streams) =>
        new(
            serializer,
            new RpcPeerOptions(),
            streams,
            SendNoopAsync,
            protocolError: static (_, _, _, _) => { },
            dispatchError: static (_, _) => { });

    public static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    public static async IAsyncEnumerable<int> BlockingItems(
        TaskCompletionSource started,
        TaskCompletionSource canceled,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        started.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            yield return 1;
        }
        finally
        {
            canceled.TrySetResult();
        }
    }
}
