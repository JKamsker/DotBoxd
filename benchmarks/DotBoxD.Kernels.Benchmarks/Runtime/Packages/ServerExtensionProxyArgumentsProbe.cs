using System.Diagnostics;
using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class ServerExtensionProxyArgumentsProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;

    private static readonly ConvertPayloadArgumentsDelegate ConvertPayloadArguments =
        CreateConvertPayloadArgumentsDelegate();
    private static readonly Type[] NoPayloadParameterTypes = [];
    private static readonly Type[] OnePayloadParameterType = [typeof(int)];
    private static readonly object?[] NoArguments = [];
    private static readonly object?[] OneArgument = [42];

    private static SandboxValue[]? s_last;
    private static int s_observedLength;

    public static void Run()
    {
        _ = MeasureLegacyZeroArguments(Warmup);
        _ = MeasureCurrent(NoPayloadParameterTypes, NoArguments, Warmup);
        _ = MeasureCurrent(OnePayloadParameterType, OneArgument, Warmup);

        var legacyZero = MeasureLegacyZeroArguments(Iterations);
        var currentZero = MeasureCurrent(NoPayloadParameterTypes, NoArguments, Iterations);
        var currentOne = MeasureCurrent(OnePayloadParameterType, OneArgument, Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("legacy zero proxy args", legacyZero);
        Write("current zero proxy args", currentZero);
        Write("current one proxy arg", currentOne);
    }

#pragma warning disable MA0005 // Intentional legacy baseline: measure the removed zero-length array allocation.
    private static Measurement MeasureLegacyZeroArguments(int iterations)
        => Measure(iterations, static () => new SandboxValue[0]);
#pragma warning restore MA0005

    private static Measurement MeasureCurrent(Type[] payloadParameterTypes, object?[] arguments, int iterations)
        => Measure(iterations, () => ConvertPayloadArguments(arguments, payloadParameterTypes));

    private static Measurement Measure(int iterations, Func<SandboxValue[]> convert)
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

    private static ConvertPayloadArgumentsDelegate CreateConvertPayloadArgumentsDelegate()
    {
        var method = typeof(ServerExtensionProxy).GetMethod(
            "ConvertPayloadArguments",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (ConvertPayloadArgumentsDelegate)method.CreateDelegate(typeof(ConvertPayloadArgumentsDelegate));
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-24} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.ObservedLength,10:N0} observed");

    private delegate SandboxValue[] ConvertPayloadArgumentsDelegate(
        object?[]? args,
        Type[] payloadParameterTypes);

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int ObservedLength);
}
