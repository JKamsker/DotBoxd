using System.Diagnostics;
using System.Reflection;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class HostServiceArgumentConversionProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;

    private static readonly ConvertArgumentsDelegate ConvertArguments = CreateConvertArgumentsDelegate();
    private static readonly Type[] NoParameterTypes = [];
    private static readonly Type[] OneParameterType = [typeof(int)];
    private static readonly SandboxValue[] NoArguments = [];
    private static readonly SandboxValue[] OneArgument = [SandboxValue.FromInt32(42)];

    private static object?[]? s_last;
    private static int s_observedLength;

    public static void Run()
    {
        _ = MeasureLegacyZeroArguments(Warmup);
        _ = MeasureCurrent(NoParameterTypes, NoArguments, Warmup);
        _ = MeasureCurrent(OneParameterType, OneArgument, Warmup);

        var legacyZero = MeasureLegacyZeroArguments(Iterations);
        var currentZero = MeasureCurrent(NoParameterTypes, NoArguments, Iterations);
        var currentOne = MeasureCurrent(OneParameterType, OneArgument, Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("legacy zero host args", legacyZero);
        Write("current zero host args", currentZero);
        Write("current one host arg", currentOne);
    }

#pragma warning disable MA0005 // Intentional legacy baseline: measure the removed zero-length array allocation.
    private static Measurement MeasureLegacyZeroArguments(int iterations)
        => Measure(iterations, static () => new object?[0]);
#pragma warning restore MA0005

    private static Measurement MeasureCurrent(Type[] parameterTypes, IReadOnlyList<SandboxValue> arguments, int iterations)
        => Measure(iterations, () => ConvertArguments(parameterTypes, arguments, 0));

    private static Measurement Measure(int iterations, Func<object?[]> convert)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var observedLength = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var values = convert();
            observedLength += values.Length;
            s_last = values;
        }

        sw.Stop();
        s_observedLength = observedLength;
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            observedLength);
    }

    private static ConvertArgumentsDelegate CreateConvertArgumentsDelegate()
    {
        var factoryType = typeof(DotBoxD.Plugins.PluginServer).Assembly.GetType(
            "DotBoxD.Hosting.Execution.HostServiceBindingFactory",
            throwOnError: true)!;
        var method = factoryType.GetMethod(
            "ConvertArguments",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (ConvertArgumentsDelegate)method.CreateDelegate(typeof(ConvertArgumentsDelegate));
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-24} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.ObservedLength,10:N0} observed");

    private delegate object?[] ConvertArgumentsDelegate(
        Type[] parameterTypes,
        IReadOnlyList<SandboxValue> args,
        int startIndex);

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int ObservedLength);
}
