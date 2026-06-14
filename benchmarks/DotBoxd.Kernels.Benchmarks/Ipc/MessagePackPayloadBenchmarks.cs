namespace DotBoxd.Kernels.Benchmarks.Ipc;

using BenchmarkDotNet.Attributes;
using DotBoxd.Codecs.MessagePack;

[MemoryDiagnoser]
public class MessagePackPayloadBenchmarks
{
    private readonly ReusableBufferWriter _writer = new(128);
    private readonly MessagePackRpcSerializer _serializer = new();
    private readonly PingRequest _request = new(42, 123);
    private byte[] _serialized = [];

    [GlobalSetup]
    public void Setup()
    {
        _writer.Reset();
        _serializer.Serialize(_writer, _request);
        _serialized = _writer.WrittenMemory.ToArray();
    }

    [Benchmark(Baseline = true)]
    public int SerializeStructPayload()
    {
        _writer.Reset();
        _serializer.Serialize(_writer, _request);
        return _writer.WrittenCount;
    }

    [Benchmark]
    public PingRequest DeserializeStructPayload()
        => _serializer.Deserialize<PingRequest>(_serialized);
}
