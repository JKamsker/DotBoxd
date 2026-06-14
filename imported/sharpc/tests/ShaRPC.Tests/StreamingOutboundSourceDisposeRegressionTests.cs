using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression for <see cref="RpcOutboundStreamSet"/> disposal. When a multi-stream outbound set is
/// disposed while one pump is still running, <c>DisposeAsync</c> best-effort disposes every attachment
/// source to unblock the live pump. A sibling pump that already completed disposed its own owned source
/// in its pump finally, so the best-effort pass disposed that source a SECOND time. The library owns the
/// source (leaveOpen:false), so it must release it exactly once.
/// </summary>
public sealed class StreamingOutboundSourceDisposeRegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisposeAsync_DoesNotDoubleDisposeCompletedSiblingSource()
    {
        var streams = new RpcStreamManager(new MessagePackRpcSerializer(), SendNoopAsync, exceptionTransformer: null);
        var handleA = streams.ReserveOutbound(RpcStreamKind.Binary);
        var handleB = streams.ReserveOutbound(RpcStreamKind.Binary);
        var completed = new CountingDisposeStream();
        var blocking = new BlockingIgnoreCancelStream();
        var outbound = streams.RegisterOutbound(
            new[]
            {
                RpcStreamAttachment.FromStream(handleA, completed, leaveOpen: false),
                RpcStreamAttachment.FromStream(handleB, blocking, leaveOpen: false),
            },
            CancellationToken.None);

        outbound.Start();

        // Pump A reads 0 immediately, completes, and disposes its owned source exactly once.
        await completed.DisposedOnce.WaitAsync(TestTimeout);

        // Pump B is parked in ReadAsync ignoring cancellation, so the set still has a running pump and
        // DisposeAsync will take the "dispose every source to unblock" path.
        await blocking.ReadStarted.WaitAsync(TestTimeout);

        await outbound.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        // The completed sibling's owned source must NOT be disposed a second time by the best-effort pass.
        Assert.Equal(1, completed.DisposeCount);
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    // Reads end-of-stream immediately so its pump completes; counts how many times its owned source is
    // disposed (the attachment disposes via IAsyncDisposable.DisposeAsync). Does not chain to base so the
    // count reflects only owner-initiated disposals.
    private sealed class CountingDisposeStream : Stream
    {
        private readonly TaskCompletionSource _disposedOnce =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task DisposedOnce => _disposedOnce.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            new(0);

        public override ValueTask DisposeAsync()
        {
            CountDispose();
            return default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CountDispose();
            }
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private void CountDispose()
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                _disposedOnce.TrySetResult();
            }
        }
    }

    // Parks in ReadAsync ignoring the cancellation token so its pump stays running until the source is
    // disposed; disposal releases the parked read so the orphaned pump can unwind after the test.
    private sealed class BlockingIgnoreCancelStream : Stream
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _readReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            return new ValueTask<int>(_readReleased.Task);
        }

        public override ValueTask DisposeAsync()
        {
            _readReleased.TrySetResult(0);
            return default;
        }

        protected override void Dispose(bool disposing)
        {
            _readReleased.TrySetResult(0);
            base.Dispose(disposing);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
