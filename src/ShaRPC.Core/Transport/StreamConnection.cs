using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipes;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Transport;

/// <summary>
/// ShaRPC connection over a duplex stream, including named pipe streams.
/// </summary>
public sealed class StreamConnection : IConnection
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly string _remoteEndpoint;
    private readonly int _maxMessageSize;
    private int _disposed;

    public StreamConnection(
        Stream stream,
        string? remoteEndpoint = null,
        bool ownsStream = true,
        int maxMessageSize = MessageFramer.MaxMessageSize)
    {
        if (maxMessageSize < MessageFramer.HeaderSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMessageSize),
                maxMessageSize,
                "Maximum message size must be at least the ShaRPC header size.");
        }

        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = ownsStream;
        _remoteEndpoint = remoteEndpoint ?? "stream";
        _maxMessageSize = maxMessageSize;
    }

    public bool IsConnected =>
        Volatile.Read(ref _disposed) == 0 &&
        (_stream is not PipeStream pipe || pipe.IsConnected);

    public string RemoteEndpoint => _remoteEndpoint;

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateOutgoingFrame(data);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            if (_stream is not PipeStream)
            {
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var lengthBuffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            var read = await ReadExactAsync(_stream, lengthBuffer.AsMemory(0, 4), ct).ConfigureAwait(false);
            if (read < 4)
            {
                return Payload.Empty;
            }

            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer.AsSpan(0, 4));
            ValidateIncomingLength(totalLength);

            var frame = Payload.Rent(totalLength);
            BinaryPrimitives.WriteInt32LittleEndian(frame.Memory.Span.Slice(0, 4), totalLength);

            if (totalLength == 4)
            {
                return frame;
            }

            try
            {
                read = await ReadExactAsync(_stream, frame.Memory.Slice(4), ct).ConfigureAwait(false);
                if (read < totalLength - 4)
                {
                    frame.Dispose();
                    return Payload.Empty;
                }
            }
            catch
            {
                frame.Dispose();
                throw;
            }

            return frame;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuffer);
        }
    }

    /// <summary>
    /// Closes the connection. This operation is idempotent.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsStream)
        {
            await DisposeStreamAsync(_stream).ConfigureAwait(false);
        }

        _sendLock.Dispose();
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(StreamConnection));
        }
    }

    private void ValidateOutgoingFrame(ReadOnlyMemory<byte> data)
    {
        if (data.Length < MessageFramer.HeaderSize)
        {
            throw new InvalidDataException($"ShaRPC frame is too small: {data.Length} bytes.");
        }

        var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(data.Span.Slice(0, 4));
        if (declaredLength != data.Length)
        {
            throw new InvalidDataException(
                $"ShaRPC frame length prefix {declaredLength} does not match buffer length {data.Length}.");
        }

        ValidateIncomingLength(declaredLength);
    }

    private void ValidateIncomingLength(int totalLength)
    {
        if (totalLength < MessageFramer.HeaderSize || totalLength > _maxMessageSize)
        {
            throw new InvalidDataException($"Invalid ShaRPC frame length: {totalLength}.");
        }
    }

    private static async Task<int> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(totalRead), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private static async ValueTask DisposeStreamAsync(Stream stream)
    {
        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Closing is best-effort.
        }
    }
}
