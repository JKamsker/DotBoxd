namespace SafeIR.Benchmarks.Ipc;

internal sealed class AllocationProbeService : IAllocationProbeService
{
    public ValueTask<int> AddAsync(int value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(value + 1);
    }

    public ValueTask<PingResponse> EchoAsync(PingRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new PingResponse(request.Value + 1, request.Nonce));
    }
}
