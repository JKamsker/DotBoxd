using DotBoxd.Services;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

/// <summary>
/// Regression test for <see cref="RpcStreamManager.Stop"/> failing to clear
/// <c>_canceledOutbound</c>.
///
/// When <c>CancelOutbound</c> is called for a reserved-but-not-yet-registered stream the stream id
/// is written into <c>_canceledOutbound</c>.  <c>Stop()</c> clears <c>_canceledInbound</c> but
/// leaves <c>_canceledOutbound</c> intact.  If <c>RegisterOutbound</c> is subsequently called for a
/// stream whose id matches a stale <c>_canceledOutbound</c> entry (from the previous connection
/// lifecycle), <c>DrainPendingOutbound</c> finds the stale entry and throws
/// <see cref="OperationCanceledException"/> — even though the cancellation predates the current
/// connection.
/// </summary>
public sealed class StreamingStopCanceledOutboundTests
{
    [Fact]
    public async Task Stop_Clears_CanceledOutbound_SoSubsequentRegisterOutboundDoesNotThrow()
    {
        // Arrange — reserve an outbound stream, then cancel it while it is still reserved.
        // This adds its id to the internal _canceledOutbound dictionary.
        var streams = CreateStreamManager();

        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        streams.CancelOutbound(handle.StreamId);

        // Act — Stop() should clear the stale _canceledOutbound entry.
        streams.Stop();

        // Arrange pt.2 — build an attachment for the same handle. _reservedOutbound is NOT cleared
        // by Stop() (separate known issue), so the reservation still exists and RegisterOutbound
        // will proceed past the duplicate-id guard into DrainPendingOutbound.
        var attachment = RpcStreamAttachment.FromStream(handle, new MemoryStream(), leaveOpen: true);

        // Assert — no OperationCanceledException should surface; the cancel is stale.
        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var outbound = streams.RegisterOutbound(
                new[] { attachment },
                CancellationToken.None);
        });

        Assert.Null(ex);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
