using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class CapabilityGrantLookupProbe
{
    private const string CapabilityId = "probe.read";
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        _ = Measure(Warmup);

        var measurement = Measure(Iterations);
        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine(
            $"RequireCapability + GetCapability {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.GrantHash,12:N0} checksum");
    }

    private static Measurement Measure(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var context = CreateContext();
        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            context.RequireCapability(CapabilityId);
            checksum ^= context.GetCapability(CapabilityId).Id.Length;
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            checksum);
    }

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxHostCalls: int.MaxValue,
            MaxWallTime: TimeSpan.FromMinutes(5));
        var policy = SandboxPolicyBuilder.Create()
            .Grant(CapabilityId, new { }, SandboxEffect.HostStateRead)
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 1)
            .Build() with
        {
            ResourceLimits = limits
        };

        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            NoopAuditSink.Instance,
            CancellationToken.None);
    }

    private sealed class NoopAuditSink : IAuditSink
    {
        public static NoopAuditSink Instance { get; } = new();
        public long EventsWritten => 0;
        public void Write(SandboxAuditEvent auditEvent) { }
        public bool HasBindingAuditSince(
            BindingDescriptor descriptor,
            long checkpoint,
            bool success,
            SandboxErrorCode? expectedErrorCode,
            SandboxRunId runId,
            string moduleHash,
            string policyHash)
            => false;
    }

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int GrantHash);
}
