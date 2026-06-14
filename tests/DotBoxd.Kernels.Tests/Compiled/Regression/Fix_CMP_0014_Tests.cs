using DotBoxd.Kernels.Transport.Ipc;
using DotBoxd.Services.Attributes;
using DotBoxd.Services.Buffers;
using DotBoxd.Services.Transport;
using System.Threading.Channels;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// CMP-0014: the DotBoxd MessagePack IPC addon advertises a transport-agnostic public surface,
/// but the maintained example only demonstrated the named-pipe convenience helpers. These tests
/// drive the generic <see cref="DotBoxdDotBoxdRpcMessagePackIpc.Listen(IServerTransport, System.Action{DotBoxd.Services.RpcPeer}, DotBoxd.Services.RpcPeerOptions?)"/>
/// and <see cref="DotBoxdDotBoxdRpcMessagePackIpc.ConnectAsync(ITransport, DotBoxd.Services.RpcPeerOptions?, System.Threading.CancellationToken)"/>
/// entry points over a deterministic in-memory (non-named-pipe) transport and exercise a
/// plugin-control-style RPC round trip. Being part of the xUnit suite, this generic-transport path
/// is now continuously checked on every supported CI operating system (Windows, Linux, macOS),
/// unlike the Windows-only named-pipe docs smoke.
/// </summary>
public sealed class Fix_CMP_0014_Tests
{
    [Fact]
    public async Task Generic_transport_drives_plugin_control_round_trip_without_named_pipes()
    {
        var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();

        await using var host = DotBoxdDotBoxdRpcMessagePackIpc.Listen(
            new SingleConnectionServerTransport(serverChannel, ownsConnection: true),
            peer => peer.Provide<IGenericPluginControl>(new GenericPluginControl()));
        await host.StartAsync();

        await using var session = await DotBoxdDotBoxdRpcMessagePackIpc.ConnectAsync(
            new SingleConnectionTransport(clientChannel, ownsConnection: true));
        var control = session.Get<IGenericPluginControl>();

        var before = await control.GetSettingAsync("MinDamage");
        Assert.Equal("100", before);

        await control.SetSettingAsync("MinDamage", "250");

        var messages = await control.PublishDamageAsync(new DamageRequest("ice", 300));
        Assert.Equal(["player: ice 300 >= 250"], messages);
    }

    [Fact]
    public async Task Generic_transport_supports_bidirectional_client_callbacks()
    {
        var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();

        await using var host = DotBoxdDotBoxdRpcMessagePackIpc.Listen(
            new SingleConnectionServerTransport(serverChannel, ownsConnection: true),
            peer => peer.Provide<IGenericNotifier>(new GenericNotifier(peer.Get<IGenericObserver>())));
        await host.StartAsync();

        await using var session = await DotBoxdDotBoxdRpcMessagePackIpc.ConnectAsync(
            new SingleConnectionTransport(clientChannel, ownsConnection: true),
            peer => peer.Provide<IGenericObserver>(new GenericObserver()));
        var notifier = session.Get<IGenericNotifier>();

        Assert.Equal("observed:ready", await notifier.NotifyAsync("ready"));
    }

    private sealed class GenericPluginControl : IGenericPluginControl
    {
        private string _minDamage = "100";

        public ValueTask<string> GetSettingAsync(string name, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(name == "MinDamage" ? _minDamage : string.Empty);
        }

        public ValueTask SetSettingAsync(string name, string value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (name == "MinDamage") {
                _minDamage = value;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<string[]> PublishDamageAsync(DamageRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var threshold = int.Parse(_minDamage);
            if (request.Amount < threshold) {
                return ValueTask.FromResult(Array.Empty<string>());
            }

            return ValueTask.FromResult(new[] { $"player: {request.DamageType} {request.Amount} >= {threshold}" });
        }
    }

    private sealed class GenericNotifier : IGenericNotifier
    {
        private readonly IGenericObserver _observer;

        public GenericNotifier(IGenericObserver observer)
        {
            _observer = observer;
        }

        public ValueTask<string> NotifyAsync(string message, CancellationToken cancellationToken = default)
            => _observer.OnNotifiedAsync(message, cancellationToken);
    }

    private sealed class GenericObserver : IGenericObserver
    {
        public ValueTask<string> OnNotifiedAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult("observed:" + message);
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

[MessagePack.MessagePackObject]
public readonly struct DamageRequest
{
    [MessagePack.SerializationConstructor]
    public DamageRequest(string damageType, int amount)
    {
        DamageType = damageType;
        Amount = amount;
    }

    [MessagePack.Key(0)]
    public string DamageType { get; }

    [MessagePack.Key(1)]
    public int Amount { get; }
}

[DotBoxdService]
public interface IGenericPluginControl
{
    ValueTask<string> GetSettingAsync(string name, CancellationToken cancellationToken = default);

    ValueTask SetSettingAsync(string name, string value, CancellationToken cancellationToken = default);

    ValueTask<string[]> PublishDamageAsync(DamageRequest request, CancellationToken cancellationToken = default);
}

[DotBoxdService]
public interface IGenericNotifier
{
    ValueTask<string> NotifyAsync(string message, CancellationToken cancellationToken = default);
}

[DotBoxdService]
public interface IGenericObserver
{
    ValueTask<string> OnNotifiedAsync(string message, CancellationToken cancellationToken = default);
}
