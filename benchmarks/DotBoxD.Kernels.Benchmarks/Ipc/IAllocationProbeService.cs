namespace DotBoxD.Kernels.Benchmarks.Ipc;

using DotBoxD.Services.Attributes;
using MessagePack;

[DotBoxDService]
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
