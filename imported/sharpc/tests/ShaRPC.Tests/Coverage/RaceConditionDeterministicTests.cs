using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using ShaRPC.Core;
using ShaRPC.Core.Generated;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.NamedPipes;
using ShaRPC.Transports.Tcp;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Cov.BugFixes;

/// <summary>
/// DETERMINISTIC red→green tests for the four race-condition fixes that a plain functional test could
/// not prove (the racy window is smaller than the surrounding async/thread-pool latency). Each uses a
/// minimal internal test seam (exposed via InternalsVisibleTo) to force the exact interleaving, so the
/// test fails on the unfixed code and passes on the fixed code — verified by reverting each fix.
/// </summary>
public sealed class RaceConditionDeterministicTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static MessagePackRpcSerializer NewSerializer() => new();

    // #2: when RpcHost raises PeerConnected, the peer's read loop must NOT have been started yet.
    //     BEFORE: peer.Start() ran inside the lock before the event -> HasStarted == true -> RED.
    [Fact]
    public async Task RpcHost_PeerConnected_FiresBeforeReadLoopStarts()
    {
        var (client, server) = InMemoryPipe.CreateConnectionPair();
        await client.DisposeAsync();

        bool? startedWhenConnected = null;
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost.Listen(new SingleConnectionServerTransport(server), NewSerializer());
        host.PeerConnected += (_, args) =>
        {
            startedWhenConnected = args.Peer.HasStarted; // internal test seam on RpcPeer
            connected.TrySetResult(true);
        };

        await host.StartAsync();
        await connected.Task.WaitAsync(Timeout);

        Assert.False(startedWhenConnected, "the read loop must not have started when PeerConnected fired");
    }

    // #5: a DisposeAsync that interleaves at connection publication must make ConnectAsync tear the
    //     published connection down and throw ObjectDisposedException.
    //     BEFORE: no post-publish re-check -> ConnectAsync returns normally -> no throw -> RED.
    [Fact]
    public async Task NamedPipeClientTransport_DisposedDuringConnect_TearsDownAndThrows()
    {
        var pipeName = "sharpc-det-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerTransport(pipeName);
        await server.StartAsync();
        var acceptTask = server.AcceptAsync();

        var client = new NamedPipeClientTransport(pipeName);
        // Seam: after _connection is published (and before the disposed re-check), dispose the transport.
        client._onConnectionPublishedForTest = () => client.DisposeAsync().AsTask();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
        Assert.False(client.IsConnected);

        try
        {
            await using var accepted = await acceptTask.WaitAsync(Timeout);
        }
        catch
        {
            // The client tore its side down mid-handshake; the server accept may fault — ignore.
        }
    }

    // #6: consuming the stashed _pendingAccept must be atomic — if a concurrent reclaim (Stop/Dispose's
    //     ObservePendingAccept) takes it inside the consume window, the claim must lose and return null,
    //     never double-take the same TcpClient that shutdown is disposing.
    //     BEFORE: non-atomic read+null returns the stashed task anyway -> double-take -> RED.
    [Fact]
    public void TcpServerTransport_PendingAcceptConsume_LosesRaceToConcurrentReclaim()
    {
        using var dummy = new TcpClient();
        var stashed = Task.FromResult(dummy);
        var server = new TcpServerTransport(IPAddress.Loopback, 0);

        server.StashPendingAcceptForTest(stashed);

        Task<TcpClient>? reclaimed = null;
        // Seam: a concurrent ObservePendingAccept reclaims the stash inside the consume window.
        server._onPendingAcceptConsumeForTest = () => reclaimed = server.ReclaimPendingAcceptForTest();

        var claimed = server.ClaimPendingAcceptForTest();

        Assert.Same(stashed, reclaimed);          // the competitor (shutdown) took the stash
        Assert.Null(claimed);                     // the atomic claim lost the race and did not double-take
    }

    // #10: fault recovery must evict only the faulted attempt this caller holds — never a successor
    //      another thread installed after the fault.
    //      BEFORE: key-only TryRemove evicts whatever is at the key -> removes the successor -> RED.
    [Fact]
    public void ShaRpcGeneratedAssemblyCatalog_FaultRecovery_PreservesSuccessor()
    {
        // A unique throwaway assembly so this never collides with any real assembly's catalog entry.
        Assembly asm = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("ShaRpcCatalogTest_" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);

        var faulted = new Lazy<bool>(() => false);   // thread A's attempt that faulted
        var successor = new Lazy<bool>(() => true);  // thread C's replacement, installed after A faulted

        ShaRpcGeneratedAssemblyCatalog.SetRegistrationAttemptForTest(asm, faulted);
        ShaRpcGeneratedAssemblyCatalog.SetRegistrationAttemptForTest(asm, successor);

        // Thread A's fault-recovery path runs now, holding only its own (faulted) Lazy.
        ShaRpcGeneratedAssemblyCatalog.EvictFaultedAttempt(asm, faulted);

        Assert.Same(successor, ShaRpcGeneratedAssemblyCatalog.GetRegistrationAttemptForTest(asm));
    }
}
