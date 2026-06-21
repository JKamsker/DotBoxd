namespace DotBoxD.Kernels.Benchmarks.Ipc.RunLocal;

using BenchmarkDotNet.Attributes;

/// <summary>
/// Allocation yardstick for the remote <c>RunLocal</c> push path (issue #60). Measures the encode-half and
/// decode-half separately so each phase's win is visible: Phase 1+2 (pooled encode + <c>ReadOnlyMemory</c>
/// contract) should drive <see cref="Encode"/> toward zero allocations for scalars; direct runtime/generated
/// decoders should drive <see cref="DecodeInvoke"/> and <see cref="DecodeInvokeGenerated"/> toward the intrinsic
/// projected-value cost (the final object plus its strings) with no <c>SandboxValue</c> graph.
/// </summary>
[MemoryDiagnoser]
public class RunLocalPushBenchmarks
{
    private RunLocalPushScenario _scenario = null!;

    [Params(
        RunLocalPushCase.Int32,
        RunLocalPushCase.String,
        RunLocalPushCase.Enum,
        RunLocalPushCase.ListInt32,
        RunLocalPushCase.Dto,
        RunLocalPushCase.AnonymousDto,
        RunLocalPushCase.WholeEvent)]
    public RunLocalPushCase Projection { get; set; }

    [GlobalSetup]
    public void Setup() => _scenario = RunLocalPushScenario.Create(Projection);

    [Benchmark]
    public int Encode() => _scenario.Encode();

    [Benchmark]
    public ValueTask DecodeInvoke() => _scenario.DecodeInvokeAsync();

    [Benchmark]
    public ValueTask DecodeInvokeGenerated() => _scenario.DecodeInvokeGeneratedAsync();

    [Benchmark]
    public ValueTask RoundTrip() => _scenario.RoundTripAsync();
}
