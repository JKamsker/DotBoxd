using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class CompiledBindingArityProbe
{
    private const string BindingId = "probe.zero";
    private const int Warmup = 20_000;
    private const int Iterations = 500_000;

    public static void Run()
    {
        _ = Measure(Warmup);

        var measurement = Measure(Iterations);
        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine(
            $"generated zero-arg binding path {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.HostCalls,12:N0} calls");
    }

    private static Measurement Measure(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var context = CreateContext();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var args = CompiledRuntime.CreateLiteralValueArray(0);
            CompiledRuntime.ChargeValueArray(context, 0);
            _ = CompiledRuntime.CallBinding(context, BindingId, args);
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            context.Budget.HostCalls);
    }

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxHostCalls: int.MaxValue,
            MaxWallTime: TimeSpan.FromMinutes(5));
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Add(Descriptor()).Build(),
            NoopAuditSink.Instance,
            CancellationToken.None);
    }

    private static BindingDescriptor Descriptor()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

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

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int HostCalls);
}
