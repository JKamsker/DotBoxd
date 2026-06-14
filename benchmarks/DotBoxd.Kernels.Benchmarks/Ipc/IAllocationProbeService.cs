namespace DotBoxd.Kernels.Benchmarks.Ipc;

using MessagePack;
using DotBoxd.Services.Attributes;

[DotBoxdService]
public interface IAllocationProbeService
{
    ValueTask<int> AddAsync(int value, CancellationToken cancellationToken = default);

    ValueTask<PingResponse> EchoAsync(PingRequest request, CancellationToken cancellationToken = default);
}

[MessagePackObject]
public readonly struct PingRequest
{
    [SerializationConstructor]
    public PingRequest(int value, long nonce)
    {
        Value = value;
        Nonce = nonce;
    }

    [Key(0)]
    public int Value { get; }

    [Key(1)]
    public long Nonce { get; }
}

[MessagePackObject]
public readonly struct PingResponse
{
    [SerializationConstructor]
    public PingResponse(int value, long nonce)
    {
        Value = value;
        Nonce = nonce;
    }

    [Key(0)]
    public int Value { get; }

    [Key(1)]
    public long Nonce { get; }
}
