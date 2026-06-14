using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.NamedPipes;
using ShaRPC.Transports.Tcp;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Cov.BugFixes;

/// <summary>
/// Concurrency stress tests for the race-condition fixes. Each asserts an invariant that holds 100% on
/// the fixed code and hammers the operation to try to provoke the racy interleaving that would violate
/// it on the unfixed code. Measured effectiveness against a reverted (buggy) build:
/// <list type="bullet">
/// <item><b>#3 NamedPipe Stop-vs-Accept</b> — RELIABLY reproduces the defect (threw NullReferenceException
/// from the disposed <c>_stopCts</c> on the first iteration); a genuine red→green proof.</item>
/// <item><b>#2 PeerConnected ordering</b> and <b>#6 TcpServer stash</b> — did NOT reproduce the race on the
/// buggy build (their windows are smaller than the surrounding async/thread-pool latency). They are kept
/// as cheap concurrency invariant guards, not as proofs of the original defect.</item>
/// </list>
/// (A NamedPipe client connect/dispose stress test was dropped: it neither reproduced its race nor ran
/// in acceptable time.)
/// </summary>
public sealed class BugFixStressTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static MessagePackRpcSerializer NewSerializer() => new();

    // #2: peer.Start() must run after PeerConnected is raised, so PeerDisconnected can never overtake
    //     PeerConnected for the same peer even when the channel is already closed.
    //     GUARD ONLY: did not reproduce the race on the unfixed code (the read loop's Task.Run latency
    //     dwarfs the lock-release-to-event-raise window), so it does not prove the defect.
    [Fact]
    public async Task Stress_PeerConnectedAlwaysPrecedesPeerDisconnected()
    {
        for (var i = 0; i < 100; i++)
        {
            var (client, server) = InMemoryPipe.CreateConnectionPair();
            await client.DisposeAsync(); // server peer's first receive returns Empty -> immediate close

            var events = new ConcurrentQueue<string>();
            var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var host = RpcHost.Listen(new SingleConnectionServerTransport(server), NewSerializer());
            host.PeerConnected += (_, _) => events.Enqueue("connected");
            host.PeerDisconnected += (_, _) =>
            {
                events.Enqueue("disconnected");
                disconnected.TrySetResult(true);
            };

            await host.StartAsync();
            await disconnected.Task.WaitAsync(Timeout);

            var ordered = events.ToArray();
            Assert.True(
                ordered.Length >= 1 && ordered[0] == "connected",
                $"iteration {i}: PeerDisconnected overtook PeerConnected — order was [{string.Join(",", ordered)}]");
        }
    }

    // #3: a Stop racing a pending Accept must surface a clean cancellation, never a NullReference or a
    //     raw ObjectDisposedException from reading a concurrently-disposed _stopCts.
    //     EFFECTIVE: on the unfixed code this threw NullReferenceException on the first iteration.
    [Fact]
    public async Task Stress_NamedPipeServer_ConcurrentAcceptAndStop_NeverThrowsNullRefOrDisposed()
    {
        for (var i = 0; i < 25; i++)
        {
            var pipeName = "sharpc-stress-" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerTransport(pipeName);
            await server.StartAsync();

            Exception? acceptEx = null;
            var accept = Task.Run(async () =>
            {
                try
                {
                    await server.AcceptAsync();
                }
                catch (Exception ex)
                {
                    acceptEx = ex;
                }
            });
            var stop = Task.Run(() => server.StopAsync());

            await Task.WhenAll(accept, stop).WaitAsync(Timeout);
            await server.DisposeAsync();

            if (acceptEx is not null)
            {
                Assert.False(
                    acceptEx is NullReferenceException,
                    $"iteration {i}: NullReferenceException from the _stopCts race");
                Assert.True(
                    acceptEx is OperationCanceledException or InvalidOperationException,
                    $"iteration {i}: expected a clean cancel/not-started, got {acceptEx.GetType().Name}: {acceptEx.Message}");
            }
        }
    }

    // #6: a cancelled-and-stashed accept consumed concurrently by AcceptAsync and Stop must never hand a
    //     disposed socket back as a live connection.
    //     GUARD ONLY: did not reproduce the double-take on the unfixed code (the consume window is too
    //     tight), so it does not prove the defect.
    [Fact]
    public async Task Stress_TcpServer_StashedAcceptRacingStop_NeverReturnsDisposedConnection()
    {
        for (var i = 0; i < 40; i++)
        {
            var server = new TcpServerTransport(IPAddress.Loopback, 0);
            await server.StartAsync();
            var port = server.LocalEndpoint!.Port;

            // Cancel an accept to stash an in-flight accept.
            using (var cts = new CancellationTokenSource())
            {
                var firstAccept = server.AcceptAsync(cts.Token);
                cts.Cancel();
                try { await firstAccept.WaitAsync(Timeout); } catch (OperationCanceledException) { }
            }

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);

            // Race a second accept (which should reuse the stashed accept) against a stop.
            IRpcChannel? accepted = null;
            var acceptTask = Task.Run(async () =>
            {
                try { accepted = await server.AcceptAsync(); } catch { /* may fault if stop won */ }
            });
            var stopTask = Task.Run(() => server.StopAsync());
            await Task.WhenAll(acceptTask, stopTask).WaitAsync(Timeout);

            // If an accept was returned, it must be a live connection — never a socket that the stop
            // path also disposed (which the non-atomic consume could produce on the unfixed code).
            if (accepted is not null)
            {
                Assert.True(accepted.IsConnected, $"iteration {i}: accept returned a disposed connection");
                await accepted.DisposeAsync();
            }

            await server.DisposeAsync();
        }
    }
}
