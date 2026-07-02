using System.Diagnostics;
using System.Reflection;
using DotBoxD.Hosting.Http;

namespace DotBoxD.Kernels.Benchmarks.Http;

internal static class HttpRedirectValidationProbe
{
    private const int Warmup = 10_000;
    private const int Iterations = 1_000_000;
    private static readonly Uri DefaultPortUri = new("https://api.example.com/config?tenant=alpha");
    private static readonly Uri ExplicitPortUri = new("https://api.example.com:8443/config?tenant=alpha");
    private static readonly Uri ExplicitPortCopy = new("https://api.example.com:8443/config?tenant=alpha");
    private static readonly Func<Uri, Uri, bool> SameUri = CreateSameUriDelegate();

    public static void Run()
    {
        _ = Measure(Warmup, "same reference default port", DefaultPortUri, DefaultPortUri);
        _ = Measure(Warmup, "same reference explicit port", ExplicitPortUri, ExplicitPortUri);
        _ = Measure(Warmup, "equal value explicit port", ExplicitPortUri, ExplicitPortCopy);

        var defaultSameReference = Measure(
            Iterations,
            "same reference default port",
            DefaultPortUri,
            DefaultPortUri);
        var explicitSameReference = Measure(
            Iterations,
            "same reference explicit port",
            ExplicitPortUri,
            ExplicitPortUri);
        var explicitEqualValue = Measure(
            Iterations,
            "equal value explicit port",
            ExplicitPortUri,
            ExplicitPortCopy);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write(defaultSameReference);
        Write(explicitSameReference);
        Write(explicitEqualValue);
    }

    private static Measurement Measure(int iterations, string name, Uri left, Uri right)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var matches = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (SameUri(left, right))
            {
                matches++;
            }
        }

        sw.Stop();
        return new Measurement(
            name,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            matches);
    }

    private static Func<Uri, Uri, bool> CreateSameUriDelegate()
    {
        var method = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.SafeHttpUriAudit",
            throwOnError: true)!
            .GetMethod(
                "SameUri",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(Uri), typeof(Uri)])!;
        return method.CreateDelegate<Func<Uri, Uri, bool>>();
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-29} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.Milliseconds * 1_000_000 / Iterations,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.AllocatedBytes / (double)Iterations,8:N1} B/op " +
            $"{measurement.Matches,10:N0} matches");

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        int Matches);
}
