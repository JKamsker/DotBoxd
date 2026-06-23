using System.Collections.Concurrent;
using System.Threading.Channels;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;
using Shared;

namespace DotBoxD.Services.Tests.Peer;

internal static class PeerTestSupport
{
    public static MessagePackRpcSerializer NewSerializer() => new();
}

internal sealed class RecordingNotifications : IPlayerNotifications
{
    private readonly string _identity;
    public ConcurrentQueue<string> Messages { get; } = new();

    public RecordingNotifications(string identity) => _identity = identity;

    public Task NotifyAsync(string message, CancellationToken ct = default)
    {
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task<string> WhoAmIAsync(CancellationToken ct = default) => Task.FromResult(_identity);
}

/// <summary>An in-process, full-duplex <see cref="IRpcChannel"/> pair backed by two channels.</summary>
internal sealed class InMemoryChannel : IRpcChannel
{
    private readonly ChannelReader<byte[]> _inbound;
    private readonly ChannelWriter<byte[]> _outbound;
    private readonly string _name;
    private int _disposed;

    private InMemoryChannel(ChannelReader<byte[]> inbound, ChannelWriter<byte[]> outbound, string name)
    {
        _inbound = inbound;
        _outbound = outbound;
        _name = name;
    }

    public static (IRpcChannel A, IRpcChannel B) CreatePair()
    {
        var ab = Channel.CreateUnbounded<byte[]>();
        var ba = Channel.CreateUnbounded<byte[]>();
        var a = new InMemoryChannel(ba.Reader, ab.Writer, "peer-a");
        var b = new InMemoryChannel(ab.Reader, ba.Writer, "peer-b");
        return (a, b);
    }

    public bool IsConnected => Volatile.Read(ref _disposed) == 0;

    public string RemoteEndpoint => _name;

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        _outbound.TryWrite(data.ToArray());
        return Task.CompletedTask;
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        try
        {
            var bytes = await _inbound.ReadAsync(ct).ConfigureAwait(false);
            var payload = Payload.Rent(bytes.Length);
            bytes.CopyTo(payload.Memory);
            return payload;
        }
        catch (ChannelClosedException)
        {
            return Payload.Empty;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _outbound.TryComplete();
        }

        return default;
    }
}

/// <summary>
/// Server transport that yields a fixed set of pre-established connections, then blocks until stopped.
/// </summary>
internal sealed class MultiConnectionServerTransport : IServerTransport
{
    private readonly Queue<IRpcChannel> _connections;
    private readonly TaskCompletionSource<bool> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private int _disposed;

    public MultiConnectionServerTransport(IEnumerable<IRpcChannel> connections) =>
        _connections = new Queue<IRpcChannel>(connections);

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MultiConnectionServerTransport));
        }

        lock (_gate)
        {
            if (_connections.Count > 0)
            {
                return _connections.Dequeue();
            }
        }

        using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), _stopped))
        {
            await _stopped.Task.ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
        throw new OperationCanceledException();
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _stopped.TrySetResult(true);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _stopped.TrySetResult(true);
        return default;
    }
}
