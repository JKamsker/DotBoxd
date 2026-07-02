using System.Diagnostics;
using System.Reflection;
using DotBoxD.Hosting.Http;

namespace DotBoxD.Kernels.Benchmarks.Http;

internal static class HttpAllowedHostProbe
{
    private const int HostCount = 1_000;
    private const int Warmup = 100;
    private const int Iterations = 10_000;
    private static readonly Uri Target = new($"https://api-{HostCount - 1}.example.com:8443/config");
    private static readonly IReadOnlySet<string> AllowedHosts = CreateAllowedHosts();
    private static readonly Func<IReadOnlySet<string>, Uri, bool> ProductionMatch = CreateMatchDelegate();

    public static void Run()
    {
        _ = Measure(Warmup, "production allowed-host match", ProductionMatch);
        _ = Measure(Warmup, "set allowed-host match", MatchesAllowedAuthorityBySet);

        var production = Measure(Iterations, "production allowed-host match", ProductionMatch);
        var setLookup = Measure(Iterations, "set allowed-host match", MatchesAllowedAuthorityBySet);

        Console.WriteLine($"hosts = {HostCount:N0}");
        Console.WriteLine($"iterations = {Iterations:N0}");
        Write(production);
        Write(setLookup);
    }

    private static Measurement Measure(
        int iterations,
        string name,
        Func<IReadOnlySet<string>, Uri, bool> match)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var matches = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (match(AllowedHosts, Target))
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

    private static bool MatchesAllowedAuthorityBySet(IReadOnlySet<string> allowedHosts, Uri uri)
        => allowedHosts.Count > 0 && allowedHosts.Contains(NormalizedAuthority(uri));

    private static string NormalizedAuthority(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    private static IReadOnlySet<string> CreateAllowedHosts()
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < HostCount; i++)
        {
            hosts.Add($"api-{i}.example.com:8443");
        }

        return hosts;
    }

    private static Func<IReadOnlySet<string>, Uri, bool> CreateMatchDelegate()
    {
        var method = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.SafeHttpUriAudit",
            throwOnError: true)!
            .GetMethod(
                "MatchesAllowedAuthority",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(IReadOnlySet<string>), typeof(Uri)])!;
        return method.CreateDelegate<Func<IReadOnlySet<string>, Uri, bool>>();
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-31} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.Matches,10:N0} matches");

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        int Matches);
}
