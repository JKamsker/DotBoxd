using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class CompiledExecutableCacheHitAllocationTests
{
    private const int WarmupIterations = 5_000;
    private const int MeasuredIterations = 100_000;

    [Fact]
    public async Task Materialized_cache_hit_avoids_miss_candidate_allocation()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var artifact = CompiledArtifactTestFactory.LoadedAssembly(
            plan,
            CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123));
        var materializeCalls = 0;
        using var cache = new CompiledExecutableCache((candidate, _, _, _) =>
        {
            materializeCalls++;
            return ValueTask.FromResult(new MaterializedCompiledArtifact(candidate, null));
        });

        var first = await cache.GetAsync(artifact, plan, "main", CancellationToken.None);
        Assert.Equal("Miss", first.MaterializationStatus);

        await MeasureHitsAsync(cache, artifact, plan, WarmupIterations);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = await MeasureHitsAsync(cache, artifact, plan, MeasuredIterations);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var perHit = (double)allocated / MeasuredIterations;

        Console.WriteLine($"CompiledExecutableCache hit allocation: {allocated:N0} B; {perHit:N1} B/hit.");
        Assert.Equal(1, materializeCalls);
        Assert.True(
            perHit < 128D,
            $"expected same-artifact cache hits to stay near zero allocation; observed {perHit:N1} B/hit.");
        GC.KeepAlive(checksum);
    }

    private static async ValueTask<int> MeasureHitsAsync(
        CompiledExecutableCache cache,
        CompiledArtifact artifact,
        ExecutionPlan plan,
        int iterations)
    {
        var checksum = 0;
        for (var i = 0; i < iterations; i++)
        {
            var executable = await cache.GetAsync(artifact, plan, "main", CancellationToken.None);
            Assert.Equal("Hit", executable.MaterializationStatus);
            checksum += executable.Artifact.AssemblyHash.Length;
        }

        return checksum;
    }
}
