namespace SafeIR.Benchmarks.Ipc;

using BenchmarkDotNet.Attributes;
using SafeIR.Transport.Ipc;
using ShaRPC.Core;

[MemoryDiagnoser]
public class IpcRoundTripBenchmarks
{
    private readonly PingRequest _request = new(42, 123);
    private RpcPeerSession? _client;
    private RpcHost? _host;
    private IAllocationProbeService? _service;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var pipeName = "safe-ir-ipc-bench-" + Guid.NewGuid().ToString("N");
        _host = SafeIrShaRpcMessagePackIpc.ListenNamedPipe(
            pipeName,
            peer => peer.Provide<IAllocationProbeService>(new AllocationProbeService()),
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
        await _host.StartAsync().ConfigureAwait(false);

        _client = await SafeIrShaRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName)
            .ConfigureAwait(false);
        _service = _client.Get<IAllocationProbeService>();
        _ = await _service.AddAsync(1).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_client is not null) {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        if (_host is not null) {
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
}
