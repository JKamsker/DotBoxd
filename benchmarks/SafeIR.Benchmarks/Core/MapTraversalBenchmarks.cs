namespace SafeIR.Benchmarks.Core;

using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class MapTraversalBenchmarks
{
    private SandboxValue _map = SandboxValue.Unit;
    private SandboxType _mapType = SandboxType.Map(SandboxType.I32, SandboxType.I32);
    private ResourceMeter _meter = null!;

    [Params(100, 1_000, 10_000)]
    public int EntryCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _map = BuildMap(EntryCount);
        _meter = new ResourceMeter(new ResourceLimits(MaxTotalCollectionElements: EntryCount * 4L));
    }

    [IterationSetup(Target = nameof(MeterMapShape))]
    public void ResetMeter()
        => _meter = new ResourceMeter(new ResourceLimits(MaxTotalCollectionElements: EntryCount * 4L));

    [Benchmark]
    public void ValidateMapShape()
        => SandboxValueValidator.RequireType(_map, _mapType, "map type mismatch");

    [Benchmark]
    public void MeterMapShape()
        => _meter.ChargeValue(_map);

    private static SandboxValue BuildMap(int entryCount)
    {
        var values = new Dictionary<SandboxValue, SandboxValue>(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            values.Add(SandboxValue.FromInt32(i), SandboxValue.FromInt32(i));
        }

        return SandboxValue.FromMap(values, SandboxType.I32, SandboxType.I32);
    }
}
