using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.Tcp;

/// <summary>
/// TCP-based connection implementation.
/// </summary>
public sealed class TcpConnection : IConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _disposed;

    public TcpConnection(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = client.GetStream();
    }

    public bool IsConnected => _client.Connected && Volatile.Read(ref _disposed) == 0;

    public string RemoteEndpoint => _client.Client.RemoteEndPoint?.ToString() ?? "unknown";

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
        }

        await _sendLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(data, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
        }

        // Read length prefix (4 bytes)
        var lengthBuffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            var bytesRead = await ReadExactAsync(_stream, lengthBuffer.AsMemory(0, 4), ct);
            if (bytesRead < 4)
            {
                return Payload.Empty; // Connection closed
            }

            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer.AsSpan(0, 4));
            if (totalLength <= 0 || totalLength > MessageFramer.MaxMessageSize)
            {
                throw new InvalidOperationException($"Invalid message length: {totalLength}");
            }

            // Rent the full frame buffer and write back the length prefix we already consumed.
            var payload = Payload.Rent(totalLength);
            BinaryPrimitives.WriteInt32LittleEndian(payload.Memory.Span.Slice(0, 4), totalLength);

            if (totalLength > 4)
            {
                try
                {
                    bytesRead = await ReadExactAsync(_stream, payload.Memory.Slice(4), ct);
                    if (bytesRead < totalLength - 4)
                    {
                        payload.Dispose();
                        return Payload.Empty; // Connection closed
                    }
                }
                catch
                {
                    payload.Dispose();
                    throw;
                }
            }

            return payload;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuffer);
        }
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(totalRead), ct);
            if (read == 0)
            {
                return totalRead; // Connection closed
            }
            totalRead += read;
        }
        return totalRead;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return default;
        }

        try
        {
            _stream.Close();
            _client.Close();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _sendLock.Dispose();
        return default;
    }
}
