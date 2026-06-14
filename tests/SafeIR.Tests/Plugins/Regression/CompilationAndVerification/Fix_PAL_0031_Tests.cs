using System.Collections.Concurrent;
using SafeIR;
using SafeIR.Compiler;
using SafeIR.Hosting;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0031: the host-local compiled executable cache must not retain
/// every materialized artifact for the host lifetime. Materializing many unique compiled artifacts
/// through a single cache should evict (and dispose) older materialized executables once a bound
/// is exceeded, while still coalescing concurrent same-key requests.
/// </summary>
public sealed class Fix_PAL_0031_Tests
{
    // Far above any reasonable bounded-cache capacity. Each unique artifact carries a distinct
    // assembly hash and therefore occupies a distinct cache slot.
    private const int UniqueArtifactCount = 96;

    [Fact]
    public async Task Materializing_many_unique_artifacts_evicts_old_materialized_executables()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PreparePlanAsync(host);

        // Track every materialized artifact the cache produces so we can observe whether the
        // cache disposes evicted entries instead of holding all of them indefinitely.
        var materialized = new ConcurrentBag<MaterializedCompiledArtifact>();
        using var cache = new CompiledExecutableCache((candidate, _, _, _) =>
        {
            var entry = new MaterializedCompiledArtifact(candidate, null);
            materialized.Add(entry);
            return ValueTask.FromResult(entry);
        });

        for (var i = 0; i < UniqueArtifactCount; i++)
        {
            var artifact = CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 1_000 + i));
            var executable = await cache.GetAsync(artifact, plan, "main", CancellationToken.None);
            Assert.Equal("Miss", executable.MaterializationStatus);
        }

        var all = materialized.ToArray();
        Assert.Equal(UniqueArtifactCount, all.Length);

        var retained = all.Count(entry => !entry.IsDisposed);

        // Correct behavior: a bounded cache must have evicted and disposed older materialized
        // executables, so the number still retained must be strictly less than the total
        // materialized. With the current unbounded cache every entry is retained until the host
        // is disposed, so this assertion is red until eviction lands.
        Assert.True(
            retained < UniqueArtifactCount,
            $"expected the cache to evict and dispose older materialized executables, " +
            $"but all {retained} of {UniqueArtifactCount} remain alive.");
    }

    [Fact]
    public async Task Same_key_materialization_is_coalesced()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PreparePlanAsync(host);
        var artifact = CompiledArtifactTestFactory.LoadedAssembly(
            plan,
            CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 42));

        var calls = 0;
        using var cache = new CompiledExecutableCache((candidate, _, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(new MaterializedCompiledArtifact(candidate, null));
        });

        var first = await cache.GetAsync(artifact, plan, "main", CancellationToken.None);
        var second = await cache.GetAsync(artifact, plan, "main", CancellationToken.None);

        Assert.Equal("Miss", first.MaterializationStatus);
        Assert.Equal("Hit", second.MaterializationStatus);
        Assert.Equal(1, calls);
    }

    private static async Task<ExecutionPlan> PreparePlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }
}
