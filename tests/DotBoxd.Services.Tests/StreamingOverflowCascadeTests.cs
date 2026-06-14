using DotBoxd.Services;
using DotBoxd.Services.Exceptions;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

/// <summary>
/// Regression test that exposes a cascade bug in
/// <see cref="RpcStreamManager.GetRegisteredInbound"/>:
///
/// <c>GetRegisteredInbound</c> calls <c>_canceledInbound.ThrowIfOverflowed()</c>
/// unconditionally before looking up the receiver. Once the tombstone tracker has been
/// pushed past its 1024-entry capacity (by a misbehaving remote canceling 1025+ streams),
/// the <c>_overflowed</c> latch is set to <c>true</c> permanently, and every subsequent
/// call to <c>ThrowIfOverflowed</c> throws <see cref="DotBoxdRpcProtocolException"/> —
/// including calls for valid, already-registered receivers that were never canceled.
///
/// Because <c>GetRegisteredInbound</c> is how handlers consume their inbound streams,
/// the overflow caused by the remote cascades to block ALL stream consumption on the
/// local side, even for streams completely unrelated to the canceled ones.
/// </summary>
public sealed class StreamingOverflowCascadeTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Register a valid test-subject receiver (ID 1), then overflow the tombstone tracker
    /// by registering and immediately canceling 1025 additional receivers (IDs 2..1026).
    /// After the overflow latch fires, <c>GetRegisteredInbound</c> for the valid, never-
    /// canceled receiver (ID 1) MUST succeed.
    ///
    /// The test currently FAILS because <c>ThrowIfOverflowed()</c> on line 50 of
    /// <c>RpcStreamManager.cs</c> throws <see cref="DotBoxdRpcProtocolException"/> regardless
    /// of whether the looked-up stream was ever canceled.
    /// </summary>
    [Fact]
    public async Task GetRegisteredInbound_ForValidReceiver_IsNotBlockedByTombstoneOverflow()
    {
        var streams = CreateStreamManager();

        // Capture the diagnostic event that fires when the 1025th tombstone overflows
        // the tracker.  We need to wait for it so the assertion happens after the latch
        // is permanently set.
        var overflowFired = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnError(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (args.Operation == "Canceled inbound stream tracking failed")
            {
                overflowFired.TrySetResult(true);
            }
        }

        RpcDiagnostics.Error += OnError;
        try
        {
            // Register the test subject: a valid receiver that will never be canceled.
            var subjectHandle = new RpcStreamHandle(1, RpcStreamKind.Binary);
            streams.RegisterInboundResponse(subjectHandle, CancellationToken.None);

            // Fill the tombstone tracker to exactly Capacity (1024 entries) by registering
            // and immediately canceling stream IDs 2..1025.
            // Each Cancel() → RemoveCanceledInbound() → _canceledInbound.Add(streamId).
            // After 1024 adds the count == Capacity; the tracker is full but not yet overflowed.
            for (var i = 0; i < RpcCanceledInboundStreams.Capacity; i++)
            {
                var handle = new RpcStreamHandle(2 + i, RpcStreamKind.Binary);
                var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
                receiver.Cancel();
            }

            // The 1025th cancel (stream ID 1026) is the one that triggers overflow:
            //   Add() sees _streamIds.Count >= Capacity → sets _overflowed = true → throws.
            // TryCompleteForCancel catches the exception and fires the diagnostic event.
            var overflowHandle = new RpcStreamHandle(1026, RpcStreamKind.Binary);
            var overflowReceiver = streams.RegisterInboundResponse(overflowHandle, CancellationToken.None);
            overflowReceiver.Cancel();

            // Wait until the latch is confirmed as set before we probe the subject.
            await overflowFired.Task.WaitAsync(TestTimeout);

            // Verify the tracker is at full capacity (the overflow entry itself was not added).
            Assert.Equal(RpcCanceledInboundStreams.Capacity, streams.CanceledInboundCount);

            // Now call GetRegisteredInbound for the test subject.
            // It was registered before the overflow and was never canceled.
            // This MUST return without throwing any exception.
            //
            // BUG: GetRegisteredInbound calls _canceledInbound.ThrowIfOverflowed() at line 50
            // of RpcStreamManager.cs before looking up the receiver.  Because _overflowed is
            // permanently latched to true, this throws DotBoxdRpcProtocolException(
            //   "Canceled inbound stream tombstone capacity was exceeded.")
            // even though stream ID 1 is a perfectly valid, live, never-canceled receiver.
            var ex = Record.Exception(
                () => streams.GetRegisteredInbound(subjectHandle));

            Assert.Null(ex); // RED: DotBoxdRpcProtocolException is thrown here today.
        }
        finally
        {
            RpcDiagnostics.Error -= OnError;
        }
    }

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
