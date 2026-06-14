using SafeIR.Compiler;
using SafeIR.Hosting;
using System.Runtime.Loader;

namespace SafeIR.Tests;

public sealed class CompiledMaterializationCacheTests
{
    [Fact]
    public async Task Loaded_assembly_materialization_is_reused_within_host()
    {
        var compiler = new ReusingLoadedAssemblyCompiler();
        using var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

        var first = await ExecuteCompiledAsync(host, plan, input);
        var second = await ExecuteCompiledAsync(host, plan, input);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(2, compiler.Calls);
        Assert.Equal("Miss", Summary(first).Fields!["materializationStatus"]);
        Assert.Equal("Hit", Summary(second).Fields!["materializationStatus"]);
    }

    [Fact]
    public async Task Materialization_cache_does_not_bypass_current_artifact_hash_validation()
    {
        var compiler = new MutatesSecondArtifactCompiler();
        using var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

        var first = await ExecuteCompiledAsync(host, plan, input);
        var second = await ExecuteCompiledAsync(host, plan, input);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.False(second.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, second.Error!.Code);
        Assert.DoesNotContain(second.AuditEvents, e => e.Kind == "RunSummary" && e.Success);
    }

    [Fact]
    public async Task Host_dispose_releases_materialized_loaded_assembly_context()
    {
        var compiler = new ReusingLoadedAssemblyCompiler();
        var host = HostWithCompiler(compiler);
        var before = AssemblyLoadContext.All.ToArray();
        var plan = await PreparePurePlanAsync(host);
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

        var result = await ExecuteCompiledAsync(host, plan, input);
        var context = NewGeneratedContext(before, compiler.ArtifactHash!);
        var weakContext = new WeakReference(context);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        context = null!;
        host.Dispose();
        await WaitForUnloadAsync(weakContext);

        Assert.False(weakContext.IsAlive);
    }

    [Fact]
    public async Task Dispose_during_materialization_rejects_inflight_get_and_disposes_result()
    {
        using var host = HostWithCompiler(new ReusingLoadedAssemblyCompiler());
        var plan = await PreparePurePlanAsync(host);
        var artifact = CompiledArtifactTestFactory.LoadedAssembly(
            plan,
            CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MaterializedCompiledArtifact? materialized = null;
        var cache = new CompiledExecutableCache(async (candidate, _, _, _) =>
        {
            started.SetResult();
            await release.Task;
            materialized = new MaterializedCompiledArtifact(candidate, null);
            return materialized;
        });
        var getTask = cache.GetAsync(artifact, plan, "main", CancellationToken.None).AsTask();

        await started.Task;
        cache.Dispose();
        release.SetResult();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await getTask);
        Assert.True(materialized?.IsDisposed);
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

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static SandboxAuditEvent Summary(SandboxExecutionResult result)
        => Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");

    private static AssemblyLoadContext NewGeneratedContext(
        IReadOnlyCollection<AssemblyLoadContext> before,
        string assemblyHash)
    {
        var expectedName = "SafeIR.Generated.Host." + assemblyHash;
        return AssemblyLoadContext.All.Single(c =>
            !before.Contains(c) &&
            string.Equals(c.Name, expectedName, StringComparison.Ordinal));
    }

    private static async Task WaitForUnloadAsync(WeakReference weakContext)
    {
        for (var i = 0; i < 20 && weakContext.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(25);
        }
    }

    private sealed class ReusingLoadedAssemblyCompiler : ISandboxCompiler
    {
        private CompiledArtifact? _artifact;
        public int Calls { get; private set; }
        public string? ArtifactHash => _artifact?.AssemblyHash;

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
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
            if (_calls == 1)
            {
                return ValueTask.FromResult(_artifact);
            }

            return ValueTask.FromResult(_artifact with { AssemblyBytes = Mutate(_artifact.AssemblyBytes) });
        }

        private static byte[] Mutate(byte[] bytes)
        {
            var mutated = bytes.ToArray();
            mutated[0] ^= 0xff;
            return mutated;
        }
    }
}
