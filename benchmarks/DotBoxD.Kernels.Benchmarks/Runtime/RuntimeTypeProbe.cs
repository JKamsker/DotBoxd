using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;

internal static class RuntimeTypeProbe
{
    public static void Run()
    {
        const int iterations = 2_000_000;
        const int warmup = 100_000;

        _ = Measure(warmup, static () => SandboxType.Scalar("I32"));
        _ = Measure(warmup, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("I32"));
        _ = Measure(warmup, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId"));
        var i32 = SandboxValue.FromInt32(42);
        var genericI32Type = SandboxType.Scalar("I32");
        _ = Measure(warmup, () => SandboxValueValidator.RequireType(i32, genericI32Type, "probe"));
        _ = Measure(warmup, () => SandboxValueValidator.RequireType(i32, SandboxType.I32, "probe"));

        var allocatedScalar = Measure(iterations, static () => SandboxType.Scalar("I32"));
        var runtimeBuiltIn = Measure(iterations, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("I32"));
        var runtimeOpaque = Measure(iterations, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId"));
        var genericValidation = Measure(
            iterations,
            () => SandboxValueValidator.RequireType(i32, genericI32Type, "probe"));
        var builtInValidation = Measure(
            iterations,
            () => SandboxValueValidator.RequireType(i32, SandboxType.I32, "probe"));

        Console.WriteLine($"iterations = {iterations:N0}");
        Write("SandboxType.Scalar(\"I32\")", allocatedScalar);
        Write("CompiledRuntime.TypeScalar(\"I32\")", runtimeBuiltIn);
        Write("CompiledRuntime.TypeScalar(\"MonsterId\")", runtimeOpaque);
        Write("RequireType(I32, Scalar(\"I32\"))", genericValidation);
        Write("RequireType(I32, SandboxType.I32)", builtInValidation);
    }

    private static Measurement Measure(int iterations, Func<SandboxType> action)
        => Measure(iterations, () =>
        {
            var result = action();
            GC.KeepAlive(result);
        });

    private static Measurement Measure(int iterations, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            action();
        }

        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Measurement(sw.Elapsed.TotalMilliseconds, allocated);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine($"{name,-40} {measurement.Milliseconds,8:N1} ms {measurement.AllocatedBytes,14:N0} B");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes);
}
