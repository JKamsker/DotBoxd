using SafeIR.Transport.Ipc;
using ShaRPC.Core.Attributes;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;
using System.Threading.Channels;

namespace SafeIR.Tests;

public sealed class ShaRpcIpcAddonTests
{
    [Fact]
    public void Named_pipe_helpers_reject_predictable_pipe_names_by_default()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => SafeIrShaRpcMessagePackIpc.ListenNamedPipe("safeir-dev", _ => { }));

        Assert.Equal("pipeName", ex.ParamName);
    }

    [Fact]
    public async Task Named_pipe_helpers_allow_explicit_unsafe_development_pipe_names()
    {
        await using var host = SafeIrShaRpcMessagePackIpc.ListenNamedPipe(
            "safeir-dev",
            _ => { },
            SafeIrNamedPipeOptions.UnsafeDevelopment);

        Assert.NotNull(host);
    }

    [Fact]
    public async Task Generic_transport_api_connects_over_non_named_pipe_transport()
    {
        var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();
        await using var host = SafeIrShaRpcMessagePackIpc.Listen(
            new SingleConnectionServerTransport(serverChannel, ownsConnection: true),
            peer => peer.Provide<IProbeService>(new ProbeService()));
        await host.StartAsync();

        await using var session = await SafeIrShaRpcMessagePackIpc.ConnectAsync(
            new SingleConnectionTransport(clientChannel, ownsConnection: true));
        var service = session.Get<IProbeService>();

        Assert.Equal(42, await service.IncrementAsync(41));
    }

    [Fact]
    public async Task Configured_generic_client_can_provide_callback_services()
    {
        var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();
        await using var host = SafeIrShaRpcMessagePackIpc.Listen(
            new SingleConnectionServerTransport(serverChannel, ownsConnection: true),
            peer => peer.Provide<ICallbackCaller>(new CallbackCaller(peer.Get<IProbeCallback>())));
        await host.StartAsync();

        await using var session = await SafeIrShaRpcMessagePackIpc.ConnectAsync(
            new SingleConnectionTransport(clientChannel, ownsConnection: true),
            peer => peer.Provide<IProbeCallback>(new ProbeCallback()));
        var caller = session.Get<ICallbackCaller>();

        Assert.Equal(42, await caller.CallAsync(41));
    }

    private sealed class ProbeService : IProbeService
    {
        public ValueTask<int> IncrementAsync(int value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(value + 1);
        }
    }

    private sealed class CallbackCaller : ICallbackCaller
    {
        private readonly IProbeCallback _callback;

        public CallbackCaller(IProbeCallback callback)
        {
            _callback = callback;
        }

        public ValueTask<int> CallAsync(int value, CancellationToken cancellationToken = default)
            => _callback.IncrementAsync(value, cancellationToken);
    }

    private sealed class ProbeCallback : IProbeCallback
    {
        public ValueTask<int> IncrementAsync(int value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(value + 1);
        }
    }

    private sealed class InMemoryRpcChannel : IRpcChannel
    {
        private readonly ChannelReader<Payload> _inbound;
        private readonly ChannelWriter<Payload> _outbound;
        private int _disposed;

        private InMemoryRpcChannel(
            ChannelReader<Payload> inbound,
            ChannelWriter<Payload> outbound,
            string remoteEndpoint)
        {
            _inbound = inbound;
            _outbound = outbound;
            RemoteEndpoint = remoteEndpoint;
        }

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint { get; }

        public static (IRpcChannel Server, IRpcChannel Client) CreatePair()
        {
            var serverInbound = Channel.CreateUnbounded<Payload>();
            var clientInbound = Channel.CreateUnbounded<Payload>();

            return (
                new InMemoryRpcChannel(serverInbound.Reader, clientInbound.Writer, "memory://client"),
                new InMemoryRpcChannel(clientInbound.Reader, serverInbound.Writer, "memory://server"));
        }

        public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var payload = Payload.Rent(data.Length);
            data.CopyTo(payload.Memory);
            try {
                await _outbound.WriteAsync(payload, ct).ConfigureAwait(false);
            }
            catch {
                payload.Dispose();
                throw;
            }
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            try {
                return await _inbound.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException) {
                return Payload.Empty;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return ValueTask.CompletedTask;
            }

            _outbound.TryComplete();
            while (_inbound.TryRead(out var payload)) {
                payload.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}

[ShaRpcService]
public interface IProbeService
{
    ValueTask<int> IncrementAsync(int value, CancellationToken cancellationToken = default);
}

[ShaRpcService]
public interface ICallbackCaller
{
    ValueTask<int> CallAsync(int value, CancellationToken cancellationToken = default);
}

[ShaRpcService]
public interface IProbeCallback
{
    ValueTask<int> IncrementAsync(int value, CancellationToken cancellationToken = default);
}
