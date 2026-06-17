using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class StreamConnectionPeerDisposeRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task PeerDispose_CompletesForNonOwnedStreamConnectionWithBlockedReceive()
    {
        await using var stream = new DisposeUnblocksReadStream();
        var connection = new StreamConnection(stream, ownsStream: false);
        var peer = RpcPeer.Over(connection, new MessagePackRpcSerializer()).Start();
        await stream.ReadParked.WaitAsync(Timeout);

        var dispose = peer.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(dispose, Task.Delay(Timeout));
        if (!ReferenceEquals(completed, dispose))
        {
            await stream.DisposeAsync();
            await dispose.WaitAsync(Timeout);
        }

        Assert.Same(dispose, completed);
        await dispose;
    }

    private sealed class DisposeUnblocksReadStream : Stream
    {
        private readonly TaskCompletionSource<int> _read =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readParked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public Task ReadParked => _readParked.Task;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            _readParked.TrySetResult();
            return new ValueTask<int>(_read.Task);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _read.TrySetResult(0);
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
            return default;
        }
    }
}
