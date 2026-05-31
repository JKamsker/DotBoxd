using System.Threading.Channels;
using ShaRPC.Core.Buffers;

namespace ShaRPC.Core.Transport;

internal sealed class DuplexFacadeConnection : IConnection
{
    private readonly IConnection _connection;
    private readonly Channel<Payload> _inbound;
    private readonly bool _dropIncomingWhenFull;
    private int _closed;

    public DuplexFacadeConnection(IConnection connection, DuplexConnectionSplitterOptions options)
    {
        _connection = connection;
        _dropIncomingWhenFull =
            options.QueueCapacity is not null &&
            options.QueueFullMode == ShaRpcQueueFullMode.DropIncoming;
        _inbound = CreateChannel(options);
    }

    public bool IsConnected => Volatile.Read(ref _closed) == 0 && _connection.IsConnected;

    public string RemoteEndpoint => _connection.RemoteEndpoint;

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            throw new ObjectDisposedException(nameof(DuplexConnectionSplitter));
        }

        return _connection.SendAsync(data, ct);
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        while (await _inbound.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            if (_inbound.Reader.TryRead(out var frame))
            {
                return frame;
            }
        }

        return Payload.Empty;
    }

    public async ValueTask<bool> EnqueueAsync(Payload frame, CancellationToken ct)
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            return false;
        }

        if (_dropIncomingWhenFull)
        {
            return _inbound.Writer.TryWrite(frame);
        }

        try
        {
            await _inbound.Writer.WriteAsync(frame, ct).ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _inbound.Writer.TryComplete();
        }
    }

    public void DrainAndDispose()
    {
        while (_inbound.Reader.TryRead(out var frame))
        {
            frame.Dispose();
        }
    }

    public ValueTask DisposeAsync() => default;

    private static Channel<Payload> CreateChannel(DuplexConnectionSplitterOptions options)
    {
        if (options.QueueCapacity is { } capacity)
        {
            return Channel.CreateBounded<Payload>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
        }

        return Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
    }
}
