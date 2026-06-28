using System.Diagnostics;
using System.Net;
using System.Reflection;
using DotBoxD.Hosting.Http;

namespace DotBoxD.Kernels.Benchmarks.Http;

internal static class SafeIpClassifierProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    private static readonly Func<IPAddress, bool> IsNonGlobal = CreateClassifierDelegate();
    private static readonly IPAddress PublicIpv4 = IPAddress.Parse("93.184.216.34");
    private static readonly IPAddress PrivateIpv4 = IPAddress.Parse("192.168.1.10");
    private static readonly IPAddress PublicIpv6 = IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946");
    private static readonly IPAddress UniqueLocalIpv6 = IPAddress.Parse("fd00::1");
    private static readonly IPAddress MappedPublicIpv4 = IPAddress.Parse("::ffff:93.184.216.34");
    private static readonly IPAddress MappedPrivateIpv4 = IPAddress.Parse("::ffff:192.168.1.10");

    public static void Run()
    {
        _ = Measure("public IPv4", Warmup, PublicIpv4);
        _ = Measure("private IPv4", Warmup, PrivateIpv4);
        _ = Measure("public IPv6", Warmup, PublicIpv6);
        _ = Measure("unique-local IPv6", Warmup, UniqueLocalIpv6);
        _ = Measure("mapped public IPv4", Warmup, MappedPublicIpv4);
        _ = Measure("mapped private IPv4", Warmup, MappedPrivateIpv4);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write(Measure("public IPv4", Iterations, PublicIpv4));
        Write(Measure("private IPv4", Iterations, PrivateIpv4));
        Write(Measure("public IPv6", Iterations, PublicIpv6));
        Write(Measure("unique-local IPv6", Iterations, UniqueLocalIpv6));
        Write(Measure("mapped public IPv4", Iterations, MappedPublicIpv4));
        Write(Measure("mapped private IPv4", Iterations, MappedPrivateIpv4));
    }

    private static Measurement Measure(string name, int iterations, IPAddress address)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var nonGlobalCount = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (IsNonGlobal(address))
            {
                nonGlobalCount++;
            }
        }

        sw.Stop();
        return Measurement.Create(
            name,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            nonGlobalCount,
            iterations);
    }

    private static Func<IPAddress, bool> CreateClassifierDelegate()
    {
        var method = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.SafeIpAddressClassifier",
            throwOnError: true)!
            .GetMethod(
                "IsNonGlobal",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(IPAddress)])!;
        return method.CreateDelegate<Func<IPAddress, bool>>();
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-20} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op {measurement.NonGlobalCount,10:N0} non-global");

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        double NanosecondsPerOperation,
        long AllocatedBytes,
        double BytesPerOperation,
        int NonGlobalCount)
    {
        public static Measurement Create(
            string name,
            double milliseconds,
            long allocatedBytes,
            int nonGlobalCount,
            int iterations)
            => new(
                name,
                milliseconds,
                milliseconds * 1_000_000 / iterations,
                allocatedBytes,
                (double)allocatedBytes / iterations,
                nonGlobalCount);
    }
}
