using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Benchmarks.Verifier;

using System.Reflection;
using System.Reflection.Emit;
using BenchmarkDotNet.Attributes;
using DotBoxD.Kernels.Verifier;

[MemoryDiagnoser]
public class GeneratedVerifierCallBenchmarks
{
    private byte[] _assemblyBytes = [];
    private ArtifactManifest _manifest = null!;
    private readonly VerificationPolicy _policy = VerificationPolicy.BoxedValueDefaults();
    private readonly GeneratedAssemblyVerifier _verifier = new();

    [Params(100, 1_000, 10_000)]
    public int CallCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _assemblyBytes = BuildAssembly(CallCount);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_assemblyBytes)).ToLowerInvariant();
        _manifest = new ArtifactManifest(
            1,
            "benchmark",
            "module",
            "plan",
            "policy",
            "bindings",
            _policy.RuntimeFacadeHash,
            "compiler",
            "type-system",
            "effect-analysis",
            _policy.VerifierVersion,
            "1.0.0",
            "net10.0",
            [],
            hash,
            DateTimeOffset.UtcNow);
    }

    [Benchmark]
    public async ValueTask<VerificationResult> VerifyRepeatedRuntimeCalls()
        => await _verifier.VerifyAsync(_assemblyBytes, _manifest, _policy, CancellationToken.None);

    private static byte[] BuildAssembly(int callCount)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Generated" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("GeneratedModule");
        var type = module.DefineType(
            "DotBoxD.Kernels.Generated.Module_0123456789abcdef",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ValidateEntrypointInput)));
        for (var i = 0; i < callCount; i++)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel)));
        }

        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.Unit)));
        il.Emit(OpCodes.Ret);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static MethodInfo Runtime(string name)
        => typeof(Kernels.Runtime.CompiledRuntime).GetMethod(name)!;
}

internal static class GeneratedVerifierOpcodeProbe
{
    private const int InstructionCount = 10_000;
    private const int Iterations = 5_000;

    public static void Run()
    {
        var offsets = BuildOffsets(InstructionCount);
        _ = MeasureLegacyBranchFree(offsets, warmup: true);
        _ = MeasureLazyBranchFree(offsets, warmup: true);

        var legacy = MeasureLegacyBranchFree(offsets, warmup: false);
        var lazy = MeasureLazyBranchFree(offsets, warmup: false);

        Console.WriteLine($"iterations = {Iterations:N0}, instructions = {InstructionCount:N0}");
        Write("legacy branch-free offsets", legacy);
        Write("lazy branch-free offsets", lazy);
        Console.WriteLine($"saved per verification: {(legacy.AllocatedBytes - lazy.AllocatedBytes) / (double)Iterations:N1} B");
    }

    private static Measurement MeasureLegacyBranchFree(int[] offsets, bool warmup)
    {
        ForceGc();

        var iterations = warmup ? 200 : Iterations;
        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var instructionOffsets = new HashSet<int>();
            var branchTargets = new HashSet<int>();
            foreach (var offset in offsets)
            {
                instructionOffsets.Add(offset);
                checksum ^= offset;
            }

            checksum ^= branchTargets.Count;
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, checksum);
    }

    private static Measurement MeasureLazyBranchFree(int[] offsets, bool warmup)
    {
        ForceGc();

        var iterations = warmup ? 200 : Iterations;
        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            HashSet<int>? branchTargets = null;
            foreach (var offset in offsets)
            {
                checksum ^= offset;
            }

            checksum ^= branchTargets?.Count ?? 0;
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, checksum);
    }

    private static int[] BuildOffsets(int count)
    {
        var offsets = new int[count];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = i * 5;
        }

        return offsets;
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B checksum={measurement.Checksum:N0}");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int Checksum);
}
