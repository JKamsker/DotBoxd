using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Validation;

internal static class NonEmptyStructuralValidationProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 200_000;

    public static void Run()
    {
        var expectedType = CreateType();
        var value = CreateValue();
        var descriptor = Descriptor(expectedType);

        _ = MeasureRequireType(value, expectedType, Warmup);
        _ = MeasureChargeReturn(value, descriptor, Warmup);

        var requireType = MeasureRequireType(value, expectedType, Iterations);
        var chargeReturn = MeasureChargeReturn(value, descriptor, Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("RequireType nested", requireType);
        Write("ChargeReturn nested", chargeReturn);
    }

    private static SandboxType CreateType()
        => SandboxType.Record([
            SandboxType.Map(SandboxType.String, SandboxType.I32),
            SandboxType.List(SandboxType.Record([
                SandboxType.I32,
                SandboxType.String,
                SandboxType.Map(SandboxType.String, SandboxType.I64)
            ]))
        ]);

    private static SandboxValue CreateValue()
        => SandboxValue.FromRecord([
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("one")] = SandboxValue.FromInt32(1),
                    [SandboxValue.FromString("two")] = SandboxValue.FromInt32(2)
                },
                SandboxType.String,
                SandboxType.I32),
            SandboxValue.FromList(
                [
                    SandboxValue.FromRecord([
                        SandboxValue.FromInt32(7),
                        SandboxValue.FromString("alpha"),
                        SandboxValue.FromMap(
                            new Dictionary<SandboxValue, SandboxValue>
                            {
                                [SandboxValue.FromString("score")] = SandboxValue.FromInt64(42)
                            },
                            SandboxType.String,
                            SandboxType.I64)
                    ])
                ],
                SandboxType.Record([
                    SandboxType.I32,
                    SandboxType.String,
                    SandboxType.Map(SandboxType.String, SandboxType.I64)
                ]))
        ]);

    private static Measurement MeasureRequireType(
        SandboxValue value,
        SandboxType expectedType,
        int iterations)
        => Measure(iterations, static state =>
            SandboxValueValidator.RequireType(state.Value, state.ExpectedType, "bad input"),
            new RequireTypeState(value, expectedType));

    private static Measurement MeasureChargeReturn(
        SandboxValue value,
        BindingDescriptor descriptor,
        int iterations)
    {
        var context = CreateContext();
        return Measure(iterations, static state =>
        {
            _ = state.Context.ChargeBindingReturn(state.Descriptor, state.Value);
        }, new ChargeReturnState(context, descriptor, value));
    }

    private static Measurement Measure<TState>(
        int iterations,
        Action<TState> action,
        TState state)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            action(state);
            checksum++;
        }

        sw.Stop();
        return Measurement.Create(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxTotalCollectionElements: long.MaxValue,
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

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-22} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op {measurement.Checksum,10:N0} checksum");

    private readonly record struct RequireTypeState(SandboxValue Value, SandboxType ExpectedType);

    private readonly record struct ChargeReturnState(
        SandboxContext Context,
        BindingDescriptor Descriptor,
        SandboxValue Value);

    private readonly record struct Measurement(
        double Milliseconds,
        double NanosecondsPerOperation,
        long AllocatedBytes,
        double BytesPerOperation,
        int Checksum)
    {
        public static Measurement Create(
            double milliseconds,
            long allocatedBytes,
            int checksum,
            int iterations)
            => new(
                milliseconds,
                milliseconds * 1_000_000 / iterations,
                allocatedBytes,
                (double)allocatedBytes / iterations,
                checksum);
    }
}
