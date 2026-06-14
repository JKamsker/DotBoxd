using DotBoxd.Services;
using DotBoxd.Services.Exceptions;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

/// <summary>
/// Regression test that exposes a permanent-latch bug in RpcCanceledInboundStreams:
/// once _overflowed is set to true it is never cleared by Remove(), so all subsequent
/// RegisterInbound calls fail even after entries have been removed and the count drops
/// well below Capacity.
/// </summary>
public sealed class StreamingCanceledOverflowRecoveryTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Fill the canceled-inbound tombstone tracker to its 1024-entry capacity, then
    /// remove 500 of those entries via CompleteInbound (which calls _canceledInbound.Remove).
    /// Registering a new inbound stream MUST succeed because the count (~524) is well below
    /// capacity. The test currently FAILS because _overflowed is latched to true permanently
    /// and ThrowIfOverflowed() throws on every subsequent RegisterInbound call.
    /// </summary>
    [Fact]
    public async Task RegisterInbound_AfterOverflowThenPartialDrain_Succeeds()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);

        // Collect the one diagnostic that fires when the 1025th entry tries to overflow.
        var overflowDiagnostic = new TaskCompletionSource<RpcDiagnosticErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnDiagnostic(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (args.Operation == "Canceled inbound stream tracking failed")
            {
                overflowDiagnostic.TrySetResult(args);
            }
        }

        RpcDiagnostics.Error += OnDiagnostic;
        try
        {
            // Phase 1 — fill the tracker to exactly Capacity (1024 entries).
            // Stream IDs 20_000 … 20_1023 are registered then immediately canceled.
            // Each Cancel() → RemoveCanceledInbound() → _canceledInbound.Add(streamId).
            for (var i = 0; i < RpcCanceledInboundStreams.Capacity; i++)
            {
                var handle = new RpcStreamHandle(20_000 + i, RpcStreamKind.Binary);
                var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
                receiver.Cancel();
            }

            // Phase 2 — the 1025th entry triggers the overflow latch.
            // TryCompleteForCancel catches the DotBoxdRpcProtocolException and fires the diagnostic.
            var handle1025 = new RpcStreamHandle(21_024, RpcStreamKind.Binary);
            var receiver1025 = streams.RegisterInboundResponse(handle1025, CancellationToken.None);
            receiver1025.Cancel();

            // Wait for the overflow diagnostic to confirm the latch fired.
            await overflowDiagnostic.Task.WaitAsync(TestTimeout);

            // Confirm the tracker is saturated at Capacity.
            Assert.Equal(RpcCanceledInboundStreams.Capacity, streams.CanceledInboundCount);

            // Phase 3 — drain 500 entries by delivering terminal frames for those streams.
            // CompleteInbound calls _canceledInbound.Remove(streamId), reducing count to ~524.
            for (var i = 0; i < 500; i++)
            {
                streams.CompleteInbound(20_000 + i);
            }

            // Sanity: count dropped.
            Assert.True(
                streams.CanceledInboundCount < RpcCanceledInboundStreams.Capacity,
                $"Expected count below {RpcCanceledInboundStreams.Capacity}, got {streams.CanceledInboundCount}.");

            // Phase 4 — attempt to register a brand-new inbound stream.
            // With ~524 entries in the tracker (well below 1024), this MUST succeed.
            // BUG: the permanent _overflowed latch causes ThrowIfOverflowed() to throw here.
            var newHandle = new RpcStreamHandle(22_000, RpcStreamKind.Binary);
            var ex = Record.Exception(
                () => streams.RegisterInboundResponse(newHandle, CancellationToken.None));

            Assert.Null(ex); // Fails today — DotBoxdRpcProtocolException is thrown instead.

            // Clean up so the manager does not leak the registered receiver.
            streams.CompleteInbound(newHandle.StreamId);
        }
        finally
        {
            RpcDiagnostics.Error -= OnDiagnostic;
        }
    }

    private static RpcStreamManager CreateStreamManager(MessagePackRpcSerializer serializer) =>
        new(serializer, static (_, _) => Task.CompletedTask, exceptionTransformer: null);
}
