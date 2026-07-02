using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Generated;

public sealed class CompiledMaterializationCancellationTests
{
    [Fact]
    public async Task Precancelled_artifact_cache_call_does_not_start_shared_compile_work()
    {
        var (plan, artifact) = await CreateCompiledArtifactAsync();
        var cache = new CompiledArtifactExecutionCache();
        var releaseCompile = new TaskCompletionSource<CompiledArtifact>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await cache.GetAsync(
                    plan,
                    "main",
                    _ =>
                    {
                        calls++;
                        return new ValueTask<CompiledArtifact>(releaseCompile.Task);
                    },
                    cancellation.Token)
                .AsTask());
        releaseCompile.SetResult(artifact);

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Precancelled_executable_cache_call_does_not_start_shared_materialization_work()
    {
        var (plan, artifact) = await CreateCompiledArtifactAsync();
        var cache = new CompiledExecutableExecutionCache();
        var releaseMaterialization = new TaskCompletionSource<CompiledExecutable>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await cache.GetAsync(
                    plan,
                    "main",
                    _ =>
                    {
                        calls++;
                        return new ValueTask<CompiledExecutable>(releaseMaterialization.Task);
                    },
                    cancellation.Token)
                .AsTask());
        releaseMaterialization.SetResult(new CompiledExecutable(artifact, "Miss"));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Precancelled_reflection_emit_compile_does_not_invoke_verifier()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var verifier = new CountingGeneratedAssemblyVerifier();
        var compiler = new ReflectionEmitSandboxCompiler(verifier);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await compiler.CompileAsync(plan, new CompileOptions("main"), cancellation.Token).AsTask());

        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task Cancelled_waiter_does_not_cancel_shared_materialization()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var artifact = CompiledArtifactTestFactory.LoadedAssembly(
            plan,
            CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123));
        var materializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMaterialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        CancellationToken materializationToken = default;
        using var cache = new CompiledExecutableCache(async (candidate, _, _, cancellationToken) =>
        {
            calls++;
            materializationToken = cancellationToken;
            materializationStarted.SetResult();
            await releaseMaterialization.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new MaterializedCompiledArtifact(candidate, null);
        });
        using var cancellation = new CancellationTokenSource();
        var first = cache.GetAsync(artifact, plan, "main", cancellation.Token).AsTask();

        await materializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = cache.GetAsync(artifact, plan, "main", CancellationToken.None).AsTask();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);
        Assert.False(materializationToken.IsCancellationRequested);
        releaseMaterialization.SetResult();

        var executable = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
        Assert.Equal("Hit", executable.MaterializationStatus);
    }

    private static async Task<(ExecutionPlan Plan, CompiledArtifact Artifact)> CreateCompiledArtifactAsync()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var artifact = CompiledArtifactTestFactory.LoadedAssembly(
            plan,
            CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123));

        return (plan, artifact);
    }

    private sealed class CountingGeneratedAssemblyVerifier : IGeneratedAssemblyVerifier
    {
        private readonly GeneratedAssemblyVerifier _inner = new();

        public int Calls { get; private set; }

        public ValueTask<VerificationResult> VerifyAsync(
            ReadOnlyMemory<byte> assemblyBytes,
            ArtifactManifest manifest,
            VerificationPolicy policy,
            CancellationToken cancellationToken)
        {
            Calls++;
            return _inner.VerifyAsync(assemblyBytes, manifest, policy, cancellationToken);
        }
    }
}
