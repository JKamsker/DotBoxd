using DotBoxd.Hosting;

namespace DotBoxd.Kernels.Tests;

public sealed class CompiledMaterializationCancellationTests
{
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
}
