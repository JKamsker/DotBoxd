using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 7 regression for <see cref="SingleConnectionServerTransport.AcceptAsync"/>. A parked accept awaits
/// the shared one-shot <c>_stopped</c> TCS via <c>ct.Register(... _stopped.TrySetResult)</c>. A later accept
/// passed an already-cancelled token fires that registration synchronously, permanently completing the
/// shared <c>_stopped</c> — which spuriously unblocks every other parked accept (they throw with their own,
/// never-cancelled tokens) and bricks the transport for all future accepts. A pre-cancel guard before the
/// registration (matching <c>TcpServerTransport</c>) prevents the shared TCS from being touched.
/// </summary>
public sealed class Round7_SingleConnectionPreCancelledAcceptTests
{
    [Fact]
    public async Task AcceptAsync_WithPreCancelledToken_DoesNotSpuriouslyCompleteOtherParkedAccepts()
    {
        await using var transport = new SingleConnectionServerTransport(new StubChannel());
        await transport.StartAsync();
        await transport.AcceptAsync(); // consume the single real connection

        using var aCts = new CancellationTokenSource(); // never cancelled
        var parked = transport.AcceptAsync(aCts.Token); // parks on the shared _stopped TCS

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        // The pre-cancelled accept must throw without touching the shared _stopped TCS.
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await transport.AcceptAsync(preCancelled.Token));

        // On the bug, _stopped is now completed and the parked accept faults spuriously; on the fix it
        // stays parked. Distinguish deterministically: a still-parked accept times out.
        var ex = await Record.ExceptionAsync(() => parked.WaitAsync(TimeSpan.FromMilliseconds(500)));
        Assert.IsType<TimeoutException>(ex);

        // Cleanup: a real stop completes the parked accept with cancellation.
        await transport.StopAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await parked);
    }

    private sealed class StubChannel : IRpcChannel
    {
        public bool IsConnected => true;

        public string RemoteEndpoint => "stub://remote";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) => Task.FromResult(Payload.Empty);

        public ValueTask DisposeAsync() => default;
    }
}
