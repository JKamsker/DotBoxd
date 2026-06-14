namespace DotBoxd.Kernels.Benchmarks.Plugins;

using BenchmarkDotNet.Attributes;
using DotBoxd.Plugins;

[MemoryDiagnoser]
public class ConventionEventAdapterBenchmarks
{
    private readonly IPluginEventAdapter<Event1> _event1 = new PluginEventAdapterRegistry().Resolve<Event1>();
    private readonly IPluginEventAdapter<Event5> _event5 = new PluginEventAdapterRegistry().Resolve<Event5>();
    private readonly IPluginEventAdapter<Event20> _event20 = new PluginEventAdapterRegistry().Resolve<Event20>();

    private readonly Event1 _one = new(1);
    private readonly Event5 _five = new(1, 2, 3, 4, 5);
    private readonly Event20 _twenty = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20);

    [Benchmark]
    public IReadOnlyList<SandboxValue> OneProperty()
        => _event1.ToSandboxValues(_one);

    [Benchmark]
    public IReadOnlyList<SandboxValue> FiveProperties()
        => _event5.ToSandboxValues(_five);

    [Benchmark]
    public IReadOnlyList<SandboxValue> TwentyProperties()
        => _event20.ToSandboxValues(_twenty);

    public sealed record Event1(int A);

    public sealed record Event5(int A, int B, int C, int D, int E);

    public sealed record Event20(
        int A,
        int B,
        int C,
        int D,
        int E,
        int F,
        int G,
        int H,
        int I,
        int J,
        int K,
        int L,
        int M,
        int N,
        int O,
        int P,
        int Q,
        int R,
        int S,
        int T);
}
