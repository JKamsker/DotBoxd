using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;

namespace DotBoxD.Kernels.Benchmarks.Ipc;

using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class IpcRoundTripBenchmarks
{
    private readonly PingRequest _request = new(42, 123);
    private RpcPeerSession? _client;
    private RpcHost? _host;
    private IAllocationProbeService? _service;

    [Params(false, true)]
    public bool LowAllocationProfile { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var pipeName = "dotboxd-ipc-bench-" + Guid.NewGuid().ToString("N");
        _host = RpcMessagePackIpc.ListenNamedPipe(
            pipeName,
            peer => peer.Provide<IAllocationProbeService>(new AllocationProbeService()),
            CreateServerOptions(LowAllocationProfile));
        await _host.StartAsync().ConfigureAwait(false);

        _client = await RpcMessagePackIpc.ConnectNamedPipeAsync(
                pipeName,
                CreateClientOptions(LowAllocationProfile))
            .ConfigureAwait(false);
        _service = _client.Get<IAllocationProbeService>();
        _ = await _service.AddAsync(1).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<int> IntRoundTripAsync()
        => await _service!.AddAsync(42).ConfigureAwait(false);

    [Benchmark]
    public async ValueTask<int> StructPayloadRoundTripAsync()
    {
        var response = await _service!.EchoAsync(_request).ConfigureAwait(false);
        return response.Value;
    }

    private static RpcPeerOptions CreateServerOptions(bool lowAllocationProfile)
        => lowAllocationProfile
            ? new RpcPeerOptions
            {
                DisableInboundRequestCancellation = true,
                InboundQueueCapacity = null,
                RequestTimeout = Timeout.InfiniteTimeSpan
            }
            : new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) };

    private static RpcPeerOptions? CreateClientOptions(bool lowAllocationProfile)
        => lowAllocationProfile
            ? new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                RejectInboundCalls = true,
                RequestTimeout = Timeout.InfiniteTimeSpan
            }
            : null;
}
