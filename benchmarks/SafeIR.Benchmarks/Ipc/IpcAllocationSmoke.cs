namespace SafeIR.Benchmarks.Ipc;

using System.Globalization;
using SafeIR.Transport.Ipc;

internal static class IpcAllocationSmoke
{
    private const int Iterations = 200;

    public static async Task RunAsync()
    {
        var pipeName = "safe-ir-ipc-smoke-" + Guid.NewGuid().ToString("N");
        await using var host = SafeIrShaRpcMessagePackIpc.ListenNamedPipe(
            pipeName,
            peer => peer.Provide<IAllocationProbeService>(new AllocationProbeService()));
        await host.StartAsync().ConfigureAwait(false);

        await using var client = await SafeIrShaRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName)
            .ConfigureAwait(false);
        var service = client.Get<IAllocationProbeService>();
        await service.AddAsync(1).ConfigureAwait(false);
        await service.EchoAsync(new PingRequest(1, 1)).ConfigureAwait(false);

        var intBytes = await MeasureAddAllocationsAsync(service).ConfigureAwait(false);
        var structBytes = await MeasureEchoAllocationsAsync(service).ConfigureAwait(false);

        Console.WriteLine($"IPC smoke iterations: {Iterations}");
        Console.WriteLine($"AddAsync total allocated bytes: {intBytes}");
        Console.WriteLine("AddAsync allocated bytes/call: " + FormatBytesPerCall(intBytes));
        Console.WriteLine($"EchoAsync total allocated bytes: {structBytes}");
        Console.WriteLine("EchoAsync allocated bytes/call: " + FormatBytesPerCall(structBytes));
    }

    private static string FormatBytesPerCall(long allocatedBytes)
        => (allocatedBytes / (double)Iterations).ToString("N1", CultureInfo.InvariantCulture);

    private static async Task<long> MeasureAddAllocationsAsync(IAllocationProbeService service)
    {
        Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < Iterations; i++) {
            _ = await service.AddAsync(42).ConfigureAwait(false);
        }

        var after = GC.GetTotalAllocatedBytes(precise: true);
        return after - before;
    }

    private static async Task<long> MeasureEchoAllocationsAsync(IAllocationProbeService service)
    {
        Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < Iterations; i++) {
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
}
