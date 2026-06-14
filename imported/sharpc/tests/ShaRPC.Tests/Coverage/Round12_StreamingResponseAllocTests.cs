using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Coverage;

/// <summary>
/// Round 12: InboundReceiverCount LINQ allocation.
/// </summary>
public sealed class Round12_StreamingResponseAllocTests
{
    // ────────────────────────────────────────────────────────────────────
    // PERF: InboundReceiverCount uses LINQ Count() with a predicate on
    // ConcurrentDictionary.Values, which allocates an enumerator snapshot
    // on every access. Should use _receivers.Count or a dedicated counter.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void InboundReceiverCount_DoesNotAllocateEnumerator()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        for (var i = 1; i <= 5; i++)
        {
            streams.RegisterInboundResponse(
                new RpcStreamHandle(i, RpcStreamKind.Binary),
                CancellationToken.None);
        }

        // Warm up JIT
        _ = streams.InboundReceiverCount;
        _ = streams.InboundReceiverCount;

        // Measure: InboundReceiverCount should not allocate.
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = streams.InboundReceiverCount;
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        // LINQ Count() on ConcurrentDictionary.Values allocates a snapshot + enumerator.
        // Currently allocates ~120 bytes per call (12000 bytes / 100 calls).
        // After fix, this should be near-zero.
        Assert.True(
            after - before < 200,
            $"InboundReceiverCount allocated {after - before} bytes over 100 calls; " +
            "expected near-zero (no LINQ enumeration).");

        for (var i = 1; i <= 5; i++)
        {
            streams.CompleteInbound(i);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Correctness: InboundReceiverCount returns the count of non-completed
    // receivers. Completed receivers that are still in _receivers should
    // be excluded.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void InboundReceiverCount_ExcludesCompletedReceivers()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        var r1 = streams.RegisterInboundResponse(
            new RpcStreamHandle(10, RpcStreamKind.Binary), CancellationToken.None);
        var r2 = streams.RegisterInboundResponse(
            new RpcStreamHandle(11, RpcStreamKind.Binary), CancellationToken.None);
        var r3 = streams.RegisterInboundResponse(
            new RpcStreamHandle(12, RpcStreamKind.Binary), CancellationToken.None);

        Assert.Equal(3, streams.InboundReceiverCount);

        // Complete r2 — it should no longer be counted.
        r2.Complete();

        Assert.Equal(2, streams.InboundReceiverCount);

        // Cleanup
        r1.Complete();
        r3.Complete();
    }
}
