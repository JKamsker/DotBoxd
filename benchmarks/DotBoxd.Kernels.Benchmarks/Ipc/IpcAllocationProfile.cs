namespace DotBoxd.Kernels.Benchmarks.Ipc;

using System.Globalization;
using DotBoxd.Kernels.Transport.Ipc;
using DotBoxd.Services;
using DotBoxd.Services.Transport;

internal static class IpcAllocationProfile
{
    public const string NamedPipeTransport = "namedpipe";
    public const string InMemoryTransport = "inmemory";

    public static async Task RunAsync(string transport, int iterations, bool disableTimeout, bool lowAllocationProfile)
    {
        if (iterations <= 0) {
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Iterations must be positive.");
        }

        await using var fixture = await CreateFixtureAsync(transport, disableTimeout, lowAllocationProfile).ConfigureAwait(false);
        var service = fixture.Session.Get<IAllocationProbeService>();

        await service.AddAsync(1).ConfigureAwait(false);
        await service.EchoAsync(new PingRequest(1, 1)).ConfigureAwait(false);

        var intBytes = await MeasureAddAllocationsAsync(service, iterations).ConfigureAwait(false);
        var structBytes = await MeasureEchoAllocationsAsync(service, iterations).ConfigureAwait(false);

        Console.WriteLine("IPC profile transport: " + transport);
        Console.WriteLine("IPC profile timeout: " + (disableTimeout || lowAllocationProfile ? "disabled" : "default"));
        Console.WriteLine("IPC profile low allocation: " + (lowAllocationProfile ? "enabled" : "disabled"));
        Console.WriteLine("IPC profile iterations: " + iterations.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("AddAsync total allocated bytes: " + intBytes.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("AddAsync allocated bytes/call: " + FormatBytesPerCall(intBytes, iterations));
        Console.WriteLine("EchoAsync total allocated bytes: " + structBytes.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("EchoAsync allocated bytes/call: " + FormatBytesPerCall(structBytes, iterations));
    }

    private static async Task<ProfileFixture> CreateFixtureAsync(
        string transport,
        bool disableTimeout,
        bool lowAllocationProfile)
    {
        var clientOptions = CreateClientOptions(disableTimeout, lowAllocationProfile);
        var serverOptions = CreateServerOptions(disableTimeout, lowAllocationProfile);
        if (transport.Equals(NamedPipeTransport, StringComparison.OrdinalIgnoreCase)) {
            var pipeName = "dotboxd-ipc-profile-" + Guid.NewGuid().ToString("N");
            var host = DotBoxdDotBoxdRpcMessagePackIpc.ListenNamedPipe(
                pipeName,
                peer => peer.Provide<IAllocationProbeService>(new AllocationProbeService()),
                serverOptions);
            await host.StartAsync().ConfigureAwait(false);
            var session = await DotBoxdDotBoxdRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName, clientOptions)
                .ConfigureAwait(false);
            return new ProfileFixture(host, session);
        }

        if (transport.Equals(InMemoryTransport, StringComparison.OrdinalIgnoreCase)) {
            var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();
            var host = DotBoxdDotBoxdRpcMessagePackIpc.Listen(
                new SingleConnectionServerTransport(serverChannel, ownsConnection: true),
                peer => peer.Provide<IAllocationProbeService>(new AllocationProbeService()),
                serverOptions ?? new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
            await host.StartAsync().ConfigureAwait(false);
            var session = await DotBoxdDotBoxdRpcMessagePackIpc.ConnectAsync(
                    new SingleConnectionTransport(clientChannel, ownsConnection: true),
                    clientOptions)
                .ConfigureAwait(false);
            return new ProfileFixture(host, session);
        }

        throw new ArgumentException($"Unknown IPC profile transport '{transport}'.", nameof(transport));
    }

    private static RpcPeerOptions? CreateClientOptions(bool disableTimeout, bool lowAllocationProfile)
    {
        if (!disableTimeout && !lowAllocationProfile) {
            return null;
        }

        return new RpcPeerOptions {
            EnableLowAllocationValueTaskInvocations = lowAllocationProfile,
            RejectInboundCalls = true,
            RequestTimeout = disableTimeout || lowAllocationProfile
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromSeconds(30)
        };
    }

    private static RpcPeerOptions? CreateServerOptions(bool disableTimeout, bool lowAllocationProfile)
    {
        if (lowAllocationProfile) {
            return new RpcPeerOptions {
                DisableInboundRequestCancellation = true,
                InboundQueueCapacity = null,
                RequestTimeout = Timeout.InfiniteTimeSpan,
            };
        }

        return disableTimeout
            ? new RpcPeerOptions { RequestTimeout = Timeout.InfiniteTimeSpan }
            : null;
    }

    private static string FormatBytesPerCall(long allocatedBytes, int iterations)
        => (allocatedBytes / (double)iterations).ToString("N1", CultureInfo.InvariantCulture);

    private static async Task<long> MeasureAddAllocationsAsync(IAllocationProbeService service, int iterations)
    {
        Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < iterations; i++) {
            _ = await service.AddAsync(42).ConfigureAwait(false);
        }

        var after = GC.GetTotalAllocatedBytes(precise: true);
        return after - before;
    }

    private static async Task<long> MeasureEchoAllocationsAsync(IAllocationProbeService service, int iterations)
    {
        Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < iterations; i++) {
            _ = await service.EchoAsync(new PingRequest(42, 123)).ConfigureAwait(false);
        }

        var after = GC.GetTotalAllocatedBytes(precise: true);
        return after - before;
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class ProfileFixture : IAsyncDisposable
    {
        private readonly RpcHost _host;

        public ProfileFixture(RpcHost host, RpcPeerSession session)
        {
            _host = host;
            Session = session;
        }

        public RpcPeerSession Session { get; }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync().ConfigureAwait(false);
            await _host.DisposeAsync().ConfigureAwait(false);
        }
    }
}
