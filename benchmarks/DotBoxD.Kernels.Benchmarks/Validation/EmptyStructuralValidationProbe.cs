using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Validation;

internal static class EmptyStructuralValidationProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 200_000;

    public static void Run()
    {
        var list = new Case(
            "EmptyList",
            SandboxValue.FromList([], SandboxType.I32),
            SandboxType.List(SandboxType.I32));
        var map = new Case(
            "EmptyMap",
            SandboxValue.FromMap(new Dictionary<SandboxValue, SandboxValue>(), SandboxType.String, SandboxType.I32),
            SandboxType.Map(SandboxType.String, SandboxType.I32));

        Console.WriteLine("Empty structural value validation - allocation probe");
        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"{"Case",-12} {"Path",-16} {"ms",10} {"B/op",12}");
        Console.WriteLine(new string('-', 72));

        foreach (var scenario in new[] { list, map })
        {
            Measure(scenario, "RequireType", static current =>
                SandboxValueValidator.RequireType(current.Value, current.ExpectedType, "bad input"));
            Measure(scenario, "ChargeReturn", static current =>
            {
                var context = CreateContext();
                var descriptor = Descriptor(current.ExpectedType);
                return () => _ = context.ChargeBindingReturn(descriptor, current.Value);
            });
        }
    }

    private static void Measure(Case scenario, string path, Action<Case> action)
        => Measure(scenario, path, () => action(scenario));

    private static void Measure(Case scenario, string path, Func<Case, Action> createAction)
        => Measure(scenario, path, createAction(scenario));

    private static void Measure(Case scenario, string path, Action action)
    {
        for (var i = 0; i < Warmup; i++)
        {
            action();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            action();
        }

        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var perOp = (double)allocated / Iterations;
        Console.WriteLine($"{scenario.Name,-12} {path,-16} {watch.Elapsed.TotalMilliseconds,10:N1} {perOp,12:N1}");
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

    private readonly record struct Case(string Name, SandboxValue Value, SandboxType ExpectedType);
}
