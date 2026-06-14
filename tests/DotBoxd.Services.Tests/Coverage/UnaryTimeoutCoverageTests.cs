using DotBoxd.Services;
using DotBoxd.Services.Buffers;
using DotBoxd.Services.Exceptions;
using DotBoxd.Services.Transport;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests.Cov.PeerOutbound;

public sealed class UnaryTimeoutCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task InvokeAsync_DirectCompletionTimeout_FaultsWithTimeoutException()
    {
        await using var channel = new SynchronousSendParkedReceiveChannel();
        await using var peer = RpcPeer
            .Over(
                channel,
                new MessagePackRpcSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromMilliseconds(50) })
            .Start();

        var call = peer.InvokeAsync<int>("Svc", "Op");

        await Assert.ThrowsAsync<DotBoxdRpcTimeoutException>(() => call.WaitAsync(Timeout));
        Assert.False(call.IsCanceled);
    }

    private sealed class SynchronousSendParkedReceiveChannel : IRpcChannel
    {
        private readonly TaskCompletionSource<Payload> _closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => !_closed.Task.IsCompleted;

        public string RemoteEndpoint => "timeout://parked";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            _closed.Task.WaitAsync(ct);

        public ValueTask DisposeAsync()
        {
            _closed.TrySetResult(Payload.Empty);
            return default;
        }
    }
}
