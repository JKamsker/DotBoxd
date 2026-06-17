using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Core;

internal static class BindingRegistryProbe
{
    private const int BindingCount = 1_000;
    private const int LookupIterations = 200_000;
    private const int SignaturesIterations = 5_000;

    public static void Run()
    {
        var registry = BuildRegistry();
        var ids = CreateBindingIds();

        _ = MeasureLegacyLookups(registry, ids, warmup: true);
        _ = MeasureLookups(registry, ids, warmup: true);
        _ = MeasureLegacySignatures(registry, ids, warmup: true);
        _ = MeasureSignatures(registry, warmup: true);

        var legacyLookups = MeasureLegacyLookups(registry, ids, warmup: false);
        var lookups = MeasureLookups(registry, ids, warmup: false);
        var legacySignatures = MeasureLegacySignatures(registry, ids, warmup: false);
        var signatures = MeasureSignatures(registry, warmup: false);

        Console.WriteLine($"bindings = {BindingCount:N0}");
        Write("legacy TryGet signature", legacyLookups);
        Write("cached TryGet signature", lookups);
        Write("legacy Signatures rebuild", legacySignatures);
        Write("cached Signatures property", signatures);
    }

    private static Measurement MeasureLegacyLookups(BindingRegistry registry, IReadOnlyList<string> ids, bool warmup)
    {
        var iterations = warmup ? 2_000 : LookupIterations;
        return Measure(iterations, () =>
        {
            for (var i = 0; i < iterations; i++)
            {
                GC.KeepAlive(registry.GetDescriptor(ids[i % ids.Count]).Signature);
            }
        });
    }

    private static Measurement MeasureLookups(BindingRegistry registry, IReadOnlyList<string> ids, bool warmup)
    {
        var iterations = warmup ? 2_000 : LookupIterations;
        return Measure(iterations, () =>
        {
            for (var i = 0; i < iterations; i++)
            {
                if (!registry.TryGet(ids[i % ids.Count], out var signature))
                {
                    throw new InvalidOperationException("binding missing");
                }

                GC.KeepAlive(signature);
            }
        });
    }

    private static Measurement MeasureLegacySignatures(BindingRegistry registry, IReadOnlyList<string> ids, bool warmup)
    {
        var iterations = warmup ? 100 : SignaturesIterations;
        return Measure(iterations, () =>
        {
            for (var i = 0; i < iterations; i++)
            {
                var signatures = new BindingSignature[ids.Count];
                for (var j = 0; j < signatures.Length; j++)
                {
                    signatures[j] = registry.GetDescriptor(ids[j]).Signature;
                }

                Array.Sort(
                    signatures,
                    static (left, right) => string.Compare(left.Id, right.Id, StringComparison.Ordinal));
                GC.KeepAlive(Array.AsReadOnly(signatures));
            }
        });
    }

    private static Measurement MeasureSignatures(BindingRegistry registry, bool warmup)
    {
        var iterations = warmup ? 100 : SignaturesIterations;
        return Measure(iterations, () =>
        {
            for (var i = 0; i < iterations; i++)
            {
                GC.KeepAlive(registry.Signatures);
            }
        });
    }

    private static Measurement Measure(int iterations, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return new Measurement(iterations, sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static BindingRegistry BuildRegistry()
    {
        var builder = new BindingRegistryBuilder();
        for (var i = 0; i < BindingCount; i++)
        {
            builder.Add(Descriptor(i));
        }

        return builder.Build();
    }

    private static string[] CreateBindingIds()
    {
        var ids = new string[BindingCount];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = $"probe.binding.{i}";
        }

        return ids;
    }

    private static BindingDescriptor Descriptor(int index)
        => new(
            $"probe.binding.{index}",
            SemVersion.One,
            [SandboxType.I32, SandboxType.String],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-24} {measurement.Iterations,10:N0} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B");

    private readonly record struct Measurement(int Iterations, double Milliseconds, long AllocatedBytes);
}
