using System.Buffers.Binary;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// RED regression for DEFECT #5: <see cref="StreamConnection.ReceiveAsync"/> has no mid-frame idle
/// timeout. After reading the 4-byte length prefix it rents a frame buffer and reads the body with the
/// bare caller token only — a peer that sends a valid prefix then stalls pins the rented (pooled) buffer
/// indefinitely. <c>TcpConnection</c> defends against this exact slow-loris pattern via a per-read idle
/// timeout (<c>_frameReadIdleTimeout</c> + a linked <see cref="CancellationTokenSource"/> that throws
/// <see cref="IOException"/> on stall); <see cref="StreamConnection"/> — used by
/// <c>NamedPipeServerTransport.AcceptAsync</c> for accepted connections — currently has no equivalent.
///
/// <para>
/// The test configures a short <c>frameReadIdleTimeout</c> via the internal transport seam and feeds a
/// stream that delivers a complete header then stalls its body read forever. The CORRECT (post-fix)
/// behavior is that <see cref="StreamConnection.ReceiveAsync"/> aborts the stalled body read quickly and
/// throws (an <see cref="IOException"/>) instead of hanging.
/// </para>
///
/// <para>
/// On the current (unfixed) code the configured timeout is stored but never applied to body reads, so
/// <see cref="StreamConnection.ReceiveAsync"/> never completes; the outer <c>WaitAsync</c> guard then
/// fails with <see cref="TimeoutException"/> (RED). Once the fix wires the idle timeout into the body
/// read path the receive throws an <see cref="IOException"/> well within the guard window (GREEN).
/// </para>
/// </summary>
public sealed class Round1_StreamConnectionFrameIdleTimeoutTests
{
    // The configured idle timeout the production fix must honor on body reads.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(200);

    // Outer guard so the test cannot hang the run on the unfixed code. Comfortably above the 200ms idle
    // timeout the fix should fire at, yet far below any wall-clock hang.
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task ReceiveAsync_AbortsStalledBodyRead_WhenFrameReadIdleTimeoutConfigured()
    {
        // Arrange: a stream that hands back exactly the 4-byte length prefix (declaring a valid total
        // frame larger than the header) then never completes the body read, simulating a slow-loris peer
        // that has pinned the rented frame buffer.
        var totalLength = MessageFramer.HeaderSize + 4; // valid: > header, well under the max
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, totalLength);

        await using var stream = new PrefixThenStallStream(prefix);
        await using var connection = new StreamConnection(
            stream,
            remoteEndpoint: "pipe://./stall-test",
            ownsStream: false,
            maxMessageSize: MessageFramer.MaxMessageSize,
            frameReadIdleTimeout: IdleTimeout);

        // Act + Assert: the receive must give up on the stalled body read quickly and surface an
        // IOException. WaitAsync bounds the wait so the unfixed code (which ignores the idle timeout and
        // hangs) fails fast with TimeoutException rather than blocking the whole suite.
        await Assert.ThrowsAsync<IOException>(
            () => connection.ReceiveAsync().WaitAsync(Guard));
    }

    /// <summary>
    /// Returns the supplied length-prefix bytes on the first read(s), then returns a task that never
    /// completes for the subsequent body read — unless the read is cancelled, in which case it observes
    /// cancellation. This lets a correctly-applied idle timeout cancel the read and complete the test,
    /// while leaving an un-timed read to hang.
    /// </summary>
    private sealed class PrefixThenStallStream : Stream
    {
        private readonly byte[] _prefix;
        private int _position;

        public PrefixThenStallStream(byte[] prefix) => _prefix = prefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _prefix.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_position < _prefix.Length)
            {
                var count = Math.Min(buffer.Length, _prefix.Length - _position);
                _prefix.AsSpan(_position, count).CopyTo(buffer.Span);
                _position += count;
                return count;
            }

            // Header fully delivered; the body read stalls forever. It only unblocks if cancelled, which
            // is precisely what the idle-timeout fix does via its linked CancellationTokenSource.
            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
