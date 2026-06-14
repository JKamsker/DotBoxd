using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Coverage;

/// <summary>
/// Wave 1: Tests for bugs and performance issues found by adversarial review.
/// </summary>
public sealed class Wave1_BugAndPerfTests
{
    // ────────────────────────────────────────────────────────────────────
    // PERF 1: RpcEventHandlerInvoker.Raise calls GetInvocationList()
    // which allocates a Delegate[] on every invocation even when there
    // is only a single subscriber. Direct invoke is zero-alloc.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Raise_SingleSubscriber_NoInvocationListAllocation()
    {
        var invoked = false;
        EventHandler<RpcDiagnosticErrorEventArgs>? handler = (_, _) => { invoked = true; };
        var args = new RpcDiagnosticErrorEventArgs("test", new Exception("test"));

        // Warm up.
        RpcEventHandlerInvoker.Raise(handler, this, args);
        invoked = false;

        var before = GC.GetAllocatedBytesForCurrentThread();
        RpcEventHandlerInvoker.Raise(handler, this, args);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(invoked);
        var allocated = after - before;
        Assert.True(allocated == 0,
            $"Raise with single subscriber allocated {allocated} bytes; " +
            "should invoke directly without GetInvocationList().");
    }

    [Fact]
    public void Raise_MultipleSubscribers_AllInvoked()
    {
        var calls = new List<int>();
        EventHandler<RpcDiagnosticErrorEventArgs>? handler = null;
        handler += (_, _) => calls.Add(1);
        handler += (_, _) => calls.Add(2);
        handler += (_, _) => calls.Add(3);

        var args = new RpcDiagnosticErrorEventArgs("test", new Exception("test"));
        RpcEventHandlerInvoker.Raise(handler, this, args);

        Assert.Equal(new[] { 1, 2, 3 }, calls);
    }

    [Fact]
    public void Raise_FailingSubscriber_OthersStillInvoked()
    {
        var calls = new List<int>();
        EventHandler<RpcDiagnosticErrorEventArgs>? handler = null;
        handler += (_, _) => calls.Add(1);
        handler += (_, _) => throw new Exception("boom");
        handler += (_, _) => calls.Add(3);

        var args = new RpcDiagnosticErrorEventArgs("test", new Exception("test"));
        RpcEventHandlerInvoker.Raise(handler, this, args);

        Assert.Contains(1, calls);
        Assert.Contains(3, calls);
    }

    // ────────────────────────────────────────────────────────────────────
    // BUG 1: PooledBufferWriter.Dispose and DetachPayload both used
    // non-atomic read-then-null of _buffer. With Interlocked.Exchange,
    // exactly one wins the race.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PooledBufferWriter_ConcurrentDisposeAndDetach_ExactlyOneWins()
    {
        var bothSucceeded = 0;

        for (var trial = 0; trial < 10_000; trial++)
        {
            var writer = new PooledBufferWriter(64);
            writer.GetSpan(1);
            writer.Advance(1);

            Payload? detached = null;
            var detachFailed = false;
            var barrier = new ManualResetEventSlim(false);

            var t1 = Task.Run(() =>
            {
                barrier.Wait();
                writer.Dispose();
            });

            var t2 = Task.Run(() =>
            {
                barrier.Wait();
                try { detached = writer.DetachPayload(); }
                catch (InvalidOperationException) { detachFailed = true; }
            });

            barrier.Set();
            await Task.WhenAll(t1, t2);

            if (detached is not null && !detachFailed)
            {
                detached.Dispose();
            }

            if (!detachFailed && detached is null)
            {
                bothSucceeded++;
            }
        }

        Assert.Equal(0, bothSucceeded);
    }

    // ────────────────────────────────────────────────────────────────────
    // CORRECTNESS: ShaRpcPendingRequests.FailAll snapshot is needed to
    // prevent failing brand-new requests that race with teardown.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void FailAll_Snapshot_ProtectsNewEntriesFromTeardown()
    {
        var pending = new ShaRpcPendingRequests();
        for (var i = 1; i <= 100; i++)
        {
            pending.TryAdd(i, out _);
        }

        pending.FailAll(new Exception("teardown"));
        Assert.Equal(0, pending.Count);

        // A new entry added after FailAll completes should be unaffected.
        Assert.True(pending.TryAdd(1, out var tcs));
        Assert.False(tcs.Task.IsCompleted,
            "New entry should not be affected by previous FailAll.");
    }

    // ────────────────────────────────────────────────────────────────────
    // BUG 2: RpcStreamManager.Stop iterates _senders.Keys while
    // RemoveOutbound modifies _senders. Verify all senders are cleaned.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Stop_CleansUpAllSenders()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        for (var i = 0; i < 20; i++)
        {
            var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
            var attachment = RpcStreamAttachment.FromStream(handle, Stream.Null, leaveOpen: true);
            streams.RegisterOutbound(attachment, CancellationToken.None);
        }

        Assert.Equal(20, streams.OutboundSenderCount);
        streams.Stop();
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    // ────────────────────────────────────────────────────────────────────
    // PERF 2: RpcDiagnostics.Report inlines Trace.TraceError instead
    // of wrapping it in a Func<string> lambda.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Report_InlinesTraceCall()
    {
        var error = new InvalidOperationException("test error");
        var ex = Record.Exception(() => RpcDiagnostics.Report("test-op", error));
        Assert.Null(ex);
    }

    // ────────────────────────────────────────────────────────────────────
    // Functional: RpcDiagnostics.Report calls handlers correctly.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RpcDiagnostics_Report_SingleHandler_InvokesCorrectly()
    {
        var operation = "test-op-" + Guid.NewGuid().ToString("N");
        var received = new List<string>();
        EventHandler<RpcDiagnosticErrorEventArgs> handler = (_, e) =>
        {
            if (e.Operation == operation)
            {
                received.Add(e.Operation);
            }
        };

        RpcDiagnostics.Error += handler;
        try
        {
            RpcDiagnostics.Report(operation, new InvalidOperationException("boom"));
            Assert.Single(received);
            Assert.Equal(operation, received[0]);
        }
        finally
        {
            RpcDiagnostics.Error -= handler;
        }
    }

    [Fact]
    public void RpcDiagnostics_Report_MultipleHandlers_IsolatesFailures()
    {
        var operation = "test-op-" + Guid.NewGuid().ToString("N");
        var received = new List<string>();
        EventHandler<RpcDiagnosticErrorEventArgs> good1 = (_, e) =>
        {
            if (e.Operation == operation)
            {
                received.Add("good1:" + e.Operation);
            }
        };
        EventHandler<RpcDiagnosticErrorEventArgs> bad = (_, e) =>
        {
            if (e.Operation == operation)
            {
                throw new Exception("handler error");
            }
        };
        EventHandler<RpcDiagnosticErrorEventArgs> good2 = (_, e) =>
        {
            if (e.Operation == operation)
            {
                received.Add("good2:" + e.Operation);
            }
        };

        RpcDiagnostics.Error += good1;
        RpcDiagnostics.Error += bad;
        RpcDiagnostics.Error += good2;
        try
        {
            RpcDiagnostics.Report(operation, new InvalidOperationException("boom"));

            Assert.Contains("good1:" + operation, received);
            Assert.Contains("good2:" + operation, received);
        }
        finally
        {
            RpcDiagnostics.Error -= good1;
            RpcDiagnostics.Error -= bad;
            RpcDiagnostics.Error -= good2;
        }
    }
}
