namespace SafeIR.Benchmarks.Plugins;

using BenchmarkDotNet.Attributes;
using SafeIR.Plugins;

[MemoryDiagnoser]
public class LiveSettingProxyBenchmarks
{
    private IBenchmarkSettings _settings = null!;

    [Params(1_000, 100_000)]
    public int Iterations { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var store = new LiveSettingStore([
            new LiveValue<int>("MinDamage", 100),
            new LiveValue<string>("DamageType", "fire"),
            new LiveValue<bool>("Enabled", true)
        ]);
        _settings = store.As<IBenchmarkSettings>();
    }

    [Benchmark]
    public int GetSettings()
    {
        var total = 0;
        for (var i = 0; i < Iterations; i++)
        {
            total += _settings.MinDamage;
            if (_settings.Enabled)
            {
                total += _settings.DamageType.Length;
            }
        }

        return total;
    }

    [Benchmark]
    public int SetSettings()
    {
        for (var i = 0; i < Iterations; i++)
        {
            _settings.MinDamage = i;
            _settings.Enabled = (i & 1) == 0;
            _settings.DamageType = "fire";
        }

        return _settings.MinDamage;
    }

    public interface IBenchmarkSettings
    {
        int MinDamage { get; set; }
        string DamageType { get; set; }
        bool Enabled { get; set; }
    }
}
