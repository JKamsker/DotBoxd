using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class CompiledMaterializationCancellationTests
{
    [Fact]
    public async Task Cancelled_materialization_receives_token_and_is_removed_from_cache()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var artifact = CompiledArtifactTestFactory.LoadedAssembly(
            plan,
            CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123));
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        CancellationToken firstToken = default;
        using var cache = new CompiledExecutableCache(async (candidate, _, _, cancellationToken) =>
        {
            calls++;
            if (calls == 1)
            {
                firstToken = cancellationToken;
                firstStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }

            await releaseSecond.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new MaterializedCompiledArtifact(candidate, null);
        });
        using var cancellation = new CancellationTokenSource();
        var first = cache.GetAsync(artifact, plan, "main", cancellation.Token).AsTask();

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await first);
        Assert.True(firstToken.IsCancellationRequested);

        var second = cache.GetAsync(artifact, plan, "main", CancellationToken.None).AsTask();
        releaseSecond.SetResult();

        var executable = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.Equal("Miss", executable.MaterializationStatus);
    }
}
