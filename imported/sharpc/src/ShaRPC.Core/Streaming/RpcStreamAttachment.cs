using System.Buffers;
using System.IO.Pipelines;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Streaming;

/// <summary>
/// A local source that will be streamed over an RPC request or response.
/// </summary>
public abstract class RpcStreamAttachment
{
    private int _sourceDisposed;

    private protected RpcStreamAttachment(RpcStreamHandle handle) => Handle = handle;

    public RpcStreamHandle Handle { get; }

    public static RpcStreamAttachment FromStream(
        RpcStreamHandle handle,
        Stream stream,
        bool leaveOpen = true)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        RequireKind(handle, RpcStreamKind.Binary);
        return new StreamAttachment(handle, stream, leaveOpen);
    }

    public static RpcStreamAttachment FromPipe(
        RpcStreamHandle handle,
        Pipe pipe,
        bool completeReader = false)
    {
        if (pipe is null)
        {
            throw new ArgumentNullException(nameof(pipe));
        }

        RequireKind(handle, RpcStreamKind.Binary);
        return new PipeAttachment(handle, pipe, completeReader);
    }

    public static RpcStreamAttachment FromAsyncEnumerable<T>(
        RpcStreamHandle handle,
        IAsyncEnumerable<T> source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        RequireKind(handle, RpcStreamKind.Items);
        return new AsyncEnumerableAttachment<T>(handle, source);
    }

    internal abstract Task PumpCoreAsync(
        RpcStreamManager streams,
        ISerializer serializer,
        CancellationToken ct);

    // Releases the owned source exactly once, whether the call comes from the pump's own finally or
    // from a sibling stream's best-effort cleanup while this pump has already completed. The set owns
    // the source (leaveOpen:false / completeReader:true), so disposing it twice would violate the
    // single-ownership contract for a caller-supplied, non-idempotent source.
    internal ValueTask DisposeSourceOnceAsync() =>
        Interlocked.Exchange(ref _sourceDisposed, 1) == 0 ? DisposeSourceCoreAsync() : default;

    private protected virtual ValueTask DisposeSourceCoreAsync() => default;

    internal async ValueTask DisposeSourceBestEffortAsync(string operation)
    {
        try
        {
            await DisposeSourceOnceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report(operation, ex);
        }
    }

    private static void RequireKind(RpcStreamHandle handle, RpcStreamKind expected)
    {
        if (handle.Kind != expected)
        {
            throw new ArgumentException($"Stream handle kind must be {expected}.", nameof(handle));
        }
    }

    private sealed class StreamAttachment : RpcStreamAttachment
    {
        private const int ChunkSize = 64 * 1024;
        private readonly Stream _stream;
        private readonly bool _leaveOpen;

        public StreamAttachment(RpcStreamHandle handle, Stream stream, bool leaveOpen)
            : base(handle)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
        }

        internal override async Task PumpCoreAsync(
            RpcStreamManager streams,
            ISerializer serializer,
            CancellationToken ct)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
            try
            {
                while (true)
                {
                    var read = await _stream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return;
                    }

                    await streams.SendStreamItemAsync(Handle.StreamId, buffer.AsMemory(0, read), ct)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                await DisposeSourceOnceAsync().ConfigureAwait(false);
            }
        }

        private protected override ValueTask DisposeSourceCoreAsync() =>
            _leaveOpen ? default : DisposeStreamAsync(_stream);
    }

    private sealed class PipeAttachment : RpcStreamAttachment
    {
        private readonly Pipe _pipe;
        private readonly bool _completeReader;

        public PipeAttachment(RpcStreamHandle handle, Pipe pipe, bool completeReader)
            : base(handle)
        {
            _pipe = pipe;
            _completeReader = completeReader;
        }

        internal override async Task PumpCoreAsync(
            RpcStreamManager streams,
            ISerializer serializer,
            CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    var result = await _pipe.Reader.ReadAsync(ct).ConfigureAwait(false);
                    var buffer = result.Buffer;
                    try
                    {
                        foreach (var segment in buffer)
                        {
                            if (!segment.IsEmpty)
                            {
                                await streams.SendStreamItemAsync(Handle.StreamId, segment, ct).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        _pipe.Reader.AdvanceTo(buffer.End);
                    }

                    if (result.IsCompleted)
                    {
                        return;
                    }
                }
            }
            finally
            {
                await DisposeSourceOnceAsync().ConfigureAwait(false);
            }
        }

        private protected override ValueTask DisposeSourceCoreAsync() =>
            _completeReader ? _pipe.Reader.CompleteAsync() : default;
    }

    private sealed class AsyncEnumerableAttachment<T> : RpcStreamAttachment
    {
        private readonly IAsyncEnumerable<T> _source;

        public AsyncEnumerableAttachment(RpcStreamHandle handle, IAsyncEnumerable<T> source)
            : base(handle) =>
            _source = source;

        internal override async Task PumpCoreAsync(
            RpcStreamManager streams,
            ISerializer serializer,
            CancellationToken ct)
        {
            await foreach (var item in _source.WithCancellation(ct).ConfigureAwait(false))
            {
                await streams.SendStreamItemAsync(Handle.StreamId, item, serializer, ct).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask DisposeStreamAsync(Stream stream)
    {
        if (stream is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        stream.Dispose();
    }
}
