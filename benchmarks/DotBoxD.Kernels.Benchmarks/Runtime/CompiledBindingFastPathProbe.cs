namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Plugins;

internal static class CompiledBindingFastPathProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 200_000;

    public static void Run()
    {
        var target = SandboxValue.FromString("player-1");
        var message = SandboxValue.FromString("Ouch, fire.");

        _ = MeasureArrayBacked(Warmup, target, message);
        _ = MeasureFastPath(Warmup, target, message);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var arrayBacked = MeasureArrayBacked(Iterations, target, message);
        var fastPath = MeasureFastPath(Iterations, target, message);

        Console.WriteLine("case                         iterations   elapsed       allocated      messages       audit");
        Write("array-backed CallBinding", arrayBacked);
        Write("CallBinding2 fast path", fastPath);
        Console.WriteLine(
            $"saved per call: {(arrayBacked.AllocatedBytes - fastPath.AllocatedBytes) / (double)Iterations:N1} B");
    }

    private static RunSummary MeasureArrayBacked(
        int iterations,
        SandboxValue target,
        SandboxValue message)
    {
        var run = BindingRun.Create();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var args = CompiledRuntime.CreateValueArray(run.Context, 2);
            args[0] = target;
            args[1] = message;
            _ = CompiledRuntime.CallBinding(run.Context, PluginMessageBindings.SendBindingId, args);
        }

        sw.Stop();
        return new RunSummary(
            iterations,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            run.Messages.Sent,
            run.Audit.EventsWritten);
    }

    private static RunSummary MeasureFastPath(
        int iterations,
        SandboxValue target,
        SandboxValue message)
    {
        var run = BindingRun.Create();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            CompiledRuntime.ChargeValueArray(run.Context, 2);
            _ = CompiledRuntime.CallBinding2(
                run.Context,
                PluginMessageBindings.SendBindingId,
                target,
                message);
        }

        sw.Stop();
        return new RunSummary(
            iterations,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            run.Messages.Sent,
            run.Audit.EventsWritten);
    }

    private static void Write(string name, RunSummary summary)
        => Console.WriteLine(
            $"{name,-25} {summary.Iterations,10:N0} {summary.Milliseconds,8:N1} ms " +
            $"{summary.AllocatedBytes,13:N0} B {summary.Messages,11:N0} {summary.AuditEvents,11:N0}");

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private sealed class BindingRun
    {
        private BindingRun(SandboxContext context, CountingMessageSink messages, CountingAuditSink audit)
        {
            Context = context;
            Messages = messages;
            Audit = audit;
        }

        public SandboxContext Context { get; }
        public CountingMessageSink Messages { get; }
        public CountingAuditSink Audit { get; }

        public static BindingRun Create()
        {
            var messages = new CountingMessageSink();
            var descriptor = PluginMessageBindings.CreateSend(messages) with
            {
                CostModel = BindingCostModel.Fixed(5)
            };
            var registry = new BindingRegistryBuilder().Add(descriptor).Build();
            var policy = Policy();
            var audit = new CountingAuditSink();
            var context = new SandboxContext(
                SandboxRunId.New(),
                policy,
                new ResourceMeter(policy.ResourceLimits),
                registry,
                audit,
                CancellationToken.None);
            return new BindingRun(context, messages, audit);
        }
    }

    private sealed class CountingMessageSink : IPluginMessageSink
    {
        public int Sent { get; private set; }

        public ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingAuditSink : IAuditSink
    {
        private SandboxAuditEvent? _last;
        private long _lastSequence;
        private long _sequence;

        public long EventsWritten => _sequence;

        public void Write(SandboxAuditEvent auditEvent)
        {
            _sequence++;
            _last = auditEvent;
            _lastSequence = _sequence;
        }

        public bool HasBindingAuditSince(
            BindingDescriptor descriptor,
            long checkpoint,
            bool success,
            SandboxErrorCode? expectedErrorCode,
            SandboxRunId runId,
            string moduleHash,
            string policyHash)
            => _lastSequence > checkpoint &&
               _last is { } auditEvent &&
               auditEvent.RunId == runId &&
               auditEvent.Success == success &&
               string.Equals(auditEvent.BindingId, descriptor.Id, StringComparison.Ordinal) &&
               CapabilityMatches(auditEvent, descriptor) &&
               ResultMatches(auditEvent, success, expectedErrorCode) &&
               FieldsMatch(auditEvent, moduleHash, policyHash);

        private static bool CapabilityMatches(SandboxAuditEvent auditEvent, BindingDescriptor descriptor)
            => descriptor.RequiredCapability is null ||
               string.Equals(auditEvent.CapabilityId, descriptor.RequiredCapability, StringComparison.Ordinal);

        private static bool ResultMatches(
            SandboxAuditEvent auditEvent,
            bool success,
            SandboxErrorCode? expectedErrorCode)
            => success
                ? auditEvent.ErrorCode is null
                : auditEvent.ErrorCode == expectedErrorCode;

        private static bool FieldsMatch(SandboxAuditEvent auditEvent, string moduleHash, string policyHash)
            => auditEvent.Fields is not null &&
               auditEvent.Fields.TryGetValue("moduleHash", out var auditModuleHash) &&
               string.Equals(auditModuleHash, moduleHash, StringComparison.Ordinal) &&
               auditEvent.Fields.TryGetValue("policyHash", out var auditPolicyHash) &&
               string.Equals(auditPolicyHash, policyHash, StringComparison.Ordinal);
    }

    private readonly record struct RunSummary(
        int Iterations,
        double Milliseconds,
        long AllocatedBytes,
        int Messages,
        long AuditEvents);
}
