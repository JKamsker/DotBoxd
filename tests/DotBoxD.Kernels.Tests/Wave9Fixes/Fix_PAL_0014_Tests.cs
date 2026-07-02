using System.Threading.Channels;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Transport;

namespace DotBoxD.Kernels.Tests.Wave9Fixes;

// Regression test for PAL-0014: IPC convenience defaults bypass the low-allocation profile.
//
// The DotBoxD.Kernels DotBoxD MessagePack IPC convenience defaults (the options applied when callers pass
// no RpcPeerOptions to ConnectAsync / ListenNamedPipe etc.) use a finite RequestTimeout and do not
// set EnableLowAllocationValueTaskInvocations. Per the DotBoxD contract, the pooled low-allocation
// unary ValueTask<T> path is only taken when RequestTimeout == Timeout.InfiniteTimeSpan AND
// EnableLowAllocationValueTaskInvocations == true. Callers using the convenience helpers therefore
// silently get the higher-allocation Task<T>-backed path.
//
// This test connects two sessions over identical in-memory transports: one using the default
// convenience options (null) and one using explicit low-allocation options, then measures per-call
// allocation of a unary ValueTask<int> round trip. The CORRECT behavior the fix should establish is
// that the public convenience path IS the low-allocation path, so the default path's per-call
// allocation should be at parity with the explicit low-allocation path. It is red today because the
// default path allocates materially more per call.
[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class Fix_PAL_0014_Tests
{
    private const int WarmupIterations = 200;
    private const int MeasureIterations = 4000;

    [Fact]
    public async Task Default_convenience_options_use_the_low_allocation_unary_path()
    {
        var lowAllocPerCall = await MeasureUnaryAllocationsPerCallAsync(useLowAllocationOptions: true);
        var defaultPerCall = await MeasureUnaryAllocationsPerCallAsync(useLowAllocationOptions: false);

        // The convenience default path must not pay materially more per call than the explicit
        // low-allocation path. A generous absolute slack (128 bytes/call) absorbs measurement noise
        // while still failing on the per-call Task<T>-backed allocation the default path incurs today.
        Assert.True(
            defaultPerCall <= lowAllocPerCall + 128,
            $"Default IPC convenience options allocated {defaultPerCall:N1} bytes/call but the " +
            $"explicit low-allocation path allocated {lowAllocPerCall:N1} bytes/call. The public " +
            "convenience defaults should be on the low-allocation profile (PAL-0014).");
    }

    private static async Task<double> MeasureUnaryAllocationsPerCallAsync(bool useLowAllocationOptions)
    {
        var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();

        var serverOptions = useLowAllocationOptions
            ? new RpcPeerOptions
            {
                DisableInboundRequestCancellation = true,
                InboundQueueCapacity = null,
                RequestTimeout = Timeout.InfiniteTimeSpan
            }
            : null;

        var clientOptions = useLowAllocationOptions
            ? new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                RejectInboundCalls = true,
                RequestTimeout = Timeout.InfiniteTimeSpan
            }
            : null;

        await using var host = RpcMessagePackIpc.Listen(
            new SingleConnectionServerTransport(serverChannel, ownsConnection: true),
            peer => peer.Provide<IAllocProbe>(new AllocProbe()),
            serverOptions);
        await host.StartAsync().ConfigureAwait(false);

        await using var session = await RpcMessagePackIpc.ConnectAsync(
                new SingleConnectionTransport(clientChannel, ownsConnection: true),
                clientOptions)
            .ConfigureAwait(false);
        var service = session.Get<IAllocProbe>();

        // Warm up JIT/tiered compilation and any first-call caches so the measured window only
        // reflects steady-state per-call allocation.
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = await service.AddAsync(i).ConfigureAwait(false);
        }

        Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < MeasureIterations; i++)
        {
            _ = await service.AddAsync(i).ConfigureAwait(false);
        }

        var after = GC.GetTotalAllocatedBytes(precise: true);
        return (after - before) / (double)MeasureIterations;
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class AllocProbe : IAllocProbe
    {
        public ValueTask<int> AddAsync(int value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(value + 1);
        }
    }

    private sealed class InMemoryRpcChannel : IRpcChannel
    {
        private readonly ChannelReader<Payload> _inbound;
        private readonly ChannelWriter<Payload> _outbound;
        private int _disposed;

        private InMemoryRpcChannel(
            ChannelReader<Payload> inbound,
            ChannelWriter<Payload> outbound,
            string remoteEndpoint)
        {
            _inbound = inbound;
            _outbound = outbound;
            RemoteEndpoint = remoteEndpoint;
        }

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint { get; }

        public static (IRpcChannel Server, IRpcChannel Client) CreatePair()
        {
            var serverInbound = Channel.CreateUnbounded<Payload>();
            var clientInbound = Channel.CreateUnbounded<Payload>();

            return (
                new InMemoryRpcChannel(serverInbound.Reader, clientInbound.Writer, "memory://client"),
                new InMemoryRpcChannel(clientInbound.Reader, serverInbound.Writer, "memory://server"));
        }

        public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var payload = Payload.Rent(data.Length);
            data.CopyTo(payload.Memory);
            try
            {
                await _outbound.WriteAsync(payload, ct).ConfigureAwait(false);
            }
            catch
            {
                payload.Dispose();
                throw;
            }
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            try
            {
                return await _inbound.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            _outbound.TryComplete();
            while (_inbound.TryRead(out var payload))
            {
                payload.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}

[DotBoxDService]
public interface IAllocProbe
{
    ValueTask<int> AddAsync(int value, CancellationToken cancellationToken = default);
}
