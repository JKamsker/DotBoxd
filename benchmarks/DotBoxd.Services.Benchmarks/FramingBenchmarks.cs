using BenchmarkDotNet.Attributes;
using DotBoxd.Services.Buffers;
using DotBoxd.Services.Protocol;
using DotBoxd.Codecs.MessagePack;
using Shared;

namespace DotBoxd.Services.Benchmarks;

[MemoryDiagnoser]
public class FramingBenchmarks
{
    private readonly MessagePackRpcSerializer _serializer = new();
    private readonly RpcRequest _request = new()
    {
        MessageId = 42,
        ServiceName = "GameService",
        MethodName = "MovePlayerAsync"
    };
    private readonly MoveRequest _argument = new()
    {
        PlayerId = "p-1",
        X = 1,
        Y = 2,
        Z = 3
    };

    private Payload _frame = Payload.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _frame = MessageFramer.FrameRequest(
            _serializer,
            42,
            MessageType.Request,
            _request,
            _argument);
    }

    [GlobalCleanup]
    public void Cleanup() => _frame.Dispose();

    [Benchmark]
    public bool ParseFrameOnly() =>
        MessageFramer.TryReadFrame(
            _frame.Memory,
            out _,
            out _,
            out _,
            out _);

    [Benchmark]
    public Payload FrameRequest()
    {
        var frame = MessageFramer.FrameRequest(
            _serializer,
            42,
            MessageType.Request,
            _request,
            _argument);
        frame.Dispose();
        return Payload.Empty;
    }

    [Benchmark]
    public MoveRequest DeserializeArgument()
    {
        if (!MessageFramer.TryReadFrame(_frame.Memory, out _, out _, out _, out var payload))
        {
            throw new InvalidOperationException("Benchmark frame is invalid.");
        }

        return _serializer.Deserialize<MoveRequest>(payload);
    }
}
