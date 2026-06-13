using SafeIR.Compiler;
using SafeIR.Hosting;

namespace SafeIR.Tests;

// ALG-0017: the compiled executable cache memoizes the expected boxed/optimized cache keys per
// (plan, entrypoint) so steady-state dispatches no longer rebuild and re-hash both cache-key
// strings on every run. These tests pin the observable behavior the optimization must preserve:
// every dispatch (including a materialized-cache hit) still fails closed when the current artifact's
// bytes or manifest cache key do not match the execution plan.
public sealed class Fix_ALG_0017_Tests
{
    [Fact]
    public async Task Warm_cache_still_serves_repeated_compiled_dispatch_as_a_hit()
    {
        var compiler = new ReusingLoadedAssemblyCompiler();
        using var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);
        var input = Input();

        var first = await ExecuteCompiledAsync(host, plan, input);
        var second = await ExecuteCompiledAsync(host, plan, input);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(SandboxValue.FromInt32(123), second.Value);
        Assert.Equal("Miss", MaterializationStatus(first));
        Assert.Equal("Hit", MaterializationStatus(second));
    }

    [Fact]
    public async Task Cache_hit_with_tampered_bytes_but_same_hash_still_fails_closed()
    {
        // The second dispatch reuses the first artifact's (CacheKey, AssemblyHash), so it resolves to
        // the same materialized-cache entry, but carries mutated bytes whose real hash no longer
        // matches the claimed hash. Byte-hash validation must still run on the hit path.
        var compiler = new MutatesSecondArtifactCompiler();
        using var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);
        var input = Input();

        var first = await ExecuteCompiledAsync(host, plan, input);
        var second = await ExecuteCompiledAsync(host, plan, input);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.False(second.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, second.Error!.Code);
    }

    [Fact]
    public async Task Cache_key_validation_uses_memoized_keys_and_still_rejects_a_mismatched_cache_key()
    {
        // Warm the per-(plan, entrypoint) expected-key memo with a valid dispatch, then send an
        // artifact whose manifest cache key no longer matches the plan. The memoized expected keys
        // must still reject it instead of accepting a stale or wrong cache key.
        var compiler = new TamperCacheKeyOnSecondCompiler();
        using var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);
        var input = Input();

        var first = await ExecuteCompiledAsync(host, plan, input);
        var second = await ExecuteCompiledAsync(host, plan, input);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.False(second.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, second.Error!.Code);
    }

    private static SandboxHost HostWithCompiler(ISandboxCompiler compiler)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });

    private static async Task<ExecutionPlan> PreparePurePlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxValue Input()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static string MaterializationStatus(SandboxExecutionResult result)
        => Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary").Fields!["materializationStatus"];

    private sealed class ReusingLoadedAssemblyCompiler : ISandboxCompiler
    {
        private CompiledArtifact? _artifact;

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            _artifact ??= CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123));
            return ValueTask.FromResult(_artifact);
        }
    }

    private sealed class MutatesSecondArtifactCompiler : ISandboxCompiler
    {
        private CompiledArtifact? _artifact;
        private int _calls;

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            _calls++;
            _artifact ??= CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123));
            return _calls == 1
                ? ValueTask.FromResult(_artifact)
                : ValueTask.FromResult(_artifact with { AssemblyBytes = Mutate(_artifact.AssemblyBytes) });
        }

        private static byte[] Mutate(byte[] bytes)
        {
            var mutated = bytes.ToArray();
            mutated[0] ^= 0xff;
            return mutated;
        }
    }

    private sealed class TamperCacheKeyOnSecondCompiler : ISandboxCompiler
    {
        private int _calls;

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            _calls++;
            var artifact = CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123));
            if (_calls == 1)
            {
                return ValueTask.FromResult(artifact);
            }

            return ValueTask.FromResult(artifact with
            {
                Manifest = artifact.Manifest with { CacheKey = "stale-cache-key" }
            });
        }
    }
}
