using System.Diagnostics;
using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class BindingReturnCreditProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 500_000;

    private static readonly Func<SandboxContext, SandboxType, IDisposable?> BeginConditionalScope =
        CreateConditionalScopeDelegate();

    public static void Run()
    {
        var i32Descriptor = Descriptor(SandboxType.I32);
        var stringDescriptor = Descriptor(SandboxType.String);
        var i32 = SandboxValue.FromInt32(42);
        var text = SandboxValue.FromString("fire");

        _ = Measure(Warmup, i32Descriptor, i32, conditionalScope: false);
        _ = Measure(Warmup, i32Descriptor, i32, conditionalScope: true);
        _ = Measure(Warmup, stringDescriptor, text, conditionalScope: true);

        var legacyI32 = Measure(Iterations, i32Descriptor, i32, conditionalScope: false);
        var conditionalI32 = Measure(Iterations, i32Descriptor, i32, conditionalScope: true);
        var conditionalString = Measure(Iterations, stringDescriptor, text, conditionalScope: true);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("legacy scope I32 return", legacyI32);
        Write("conditional I32 return", conditionalI32);
        Write("conditional String return", conditionalString);
    }

    private static Measurement Measure(
        int iterations,
        BindingDescriptor descriptor,
        SandboxValue value,
        bool conditionalScope)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var context = CreateContext();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var scope = conditionalScope
                ? BeginConditionalScope(context, descriptor.ReturnType)
                : context.BeginBindingReturnCreditScope();
            _ = context.ChargeBindingReturn(descriptor, value);
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            context.Budget.StringBytes);
    }

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxTotalStringBytes: long.MaxValue);
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private static BindingDescriptor Descriptor(SandboxType returnType)
        => new(
            "probe.return",
            SemVersion.One,
            [],
            returnType,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub("Probe", "Probe"));

    private static Func<SandboxContext, SandboxType, IDisposable?> CreateConditionalScopeDelegate()
    {
        var method = typeof(SandboxContext).GetMethod(
            "BeginBindingReturnCreditScope",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(SandboxType)],
            modifiers: null)!;
        return method.CreateDelegate<Func<SandboxContext, SandboxType, IDisposable?>>();
    }

    private static void Write(string name, Measurement summary)
        => Console.WriteLine(
            $"{name,-27} {summary.Milliseconds,8:N1} ms " +
            $"{summary.AllocatedBytes,14:N0} B {summary.StringBytes,14:N0} string B");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, long StringBytes);
}
