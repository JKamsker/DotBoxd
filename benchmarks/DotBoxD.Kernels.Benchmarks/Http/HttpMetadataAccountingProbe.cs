using System.Diagnostics;
using System.Net;
using System.Reflection;
using DotBoxD.Hosting.Http;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Http;

internal static class HttpMetadataAccountingProbe
{
    private const int Warmup = 1_000;
    private const int Iterations = 100_000;

    private static readonly Func<HttpResponseMessage, long> MeasureMetadataBytes = CreateMeasureDelegate();
    private static readonly Func<SandboxContext, HttpResponseMessage, long, long> ChargeMetadata = CreateChargeDelegate();

    public static void Run()
    {
        using var response = CreateResponse();

        _ = Measure(Warmup, response, legacyDoubleMeasure: true);
        _ = Measure(Warmup, response, legacyDoubleMeasure: false);

        var legacy = Measure(Iterations, response, legacyDoubleMeasure: true);
        var current = Measure(Iterations, response, legacyDoubleMeasure: false);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("legacy Measure+ChargeMetadata", legacy);
        Write("single ChargeMetadata", current);
    }

    private static Measurement Measure(int iterations, HttpResponseMessage response, bool legacyDoubleMeasure)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var context = CreateContext();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        long bytes = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (legacyDoubleMeasure)
            {
                bytes += MeasureMetadataBytes(response);
            }

            bytes += ChargeMetadata(context, response, long.MaxValue);
        }

        sw.Stop();
        GC.KeepAlive(bytes);
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            context.Budget.NetworkBytesRead);
    }

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(MaxNetworkBytesRead: long.MaxValue);
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private static HttpResponseMessage CreateResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[128]),
            ReasonPhrase = "OK"
        };
        for (var i = 0; i < 24; i++)
        {
            response.Headers.TryAddWithoutValidation($"X-Probe-{i}", $"value-{i}");
        }

        response.Content.Headers.ContentType = new("text/plain");
        return response;
    }

    private static Func<HttpResponseMessage, long> CreateMeasureDelegate()
        => CreateDelegate<Func<HttpResponseMessage, long>>("MeasureMetadataBytes");

    private static Func<SandboxContext, HttpResponseMessage, long, long> CreateChargeDelegate()
        => CreateDelegate<Func<SandboxContext, HttpResponseMessage, long, long>>("ChargeMetadata");

    private static TDelegate CreateDelegate<TDelegate>(string methodName)
        where TDelegate : Delegate
    {
        var type = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.Internal.SafeHttpResponseAccounting",
            throwOnError: true)!;
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        return method.CreateDelegate<TDelegate>();
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-31} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.NetworkBytesRead,14:N0} network B");

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        long NetworkBytesRead);
}
