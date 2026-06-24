using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

/// <summary>
/// Regression coverage for PAL-0036: plugin kernel execution created a linked
/// <see cref="CancellationTokenSource"/> for every sandbox entrypoint invocation, even on the
/// common path where the caller supplies the default (non-cancelable) token and the only live
/// cancellation source is the kernel revocation token. The fix introduced
/// <see cref="PluginExecutionCancellation"/>, which allocates a linked source only when BOTH the
/// caller token and the revocation token can independently fire; otherwise it passes the single
/// cancelable token (or the non-cancelable caller token) through with no allocation.
///
/// These assert the resolver's contract directly. The earlier end-to-end measurement compared the
/// total per-call allocation of two interpreter executions; once unrelated per-call allocations grew,
/// the ~one-CancellationTokenSource signal fell below run-to-run noise. Testing the resolver in
/// isolation keeps the regression deterministic and immune to interpreter allocation changes.
/// </summary>
public sealed class Fix_PAL_0036_Tests
{
    private const int Iterations = 10_000;

    [Fact]
    public void NonCancelableCallerPath_doesNotAllocateLinkedCancellationSource()
    {
        using var revocation = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();

        // Common path: a non-cancelable caller token must skip the linked source entirely and pass
        // the revocation token through unchanged.
        using (var scope = PluginExecutionCancellation.Create(CancellationToken.None, revocation.Token))
        {
            Assert.Equal(revocation.Token, scope.Token);
        }

        // Warm up both paths so JIT/first-call costs are excluded from the measurement.
        for (var i = 0; i < 200; i++)
        {
            using (PluginExecutionCancellation.Create(CancellationToken.None, revocation.Token))
            { }
            using (PluginExecutionCancellation.Create(caller.Token, revocation.Token))
            { }
        }

        var beforeNonCancelable = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            using (PluginExecutionCancellation.Create(CancellationToken.None, revocation.Token))
            { }
        }
        var nonCancelablePerCall = (GC.GetAllocatedBytesForCurrentThread() - beforeNonCancelable) / Iterations;

        var beforeBoth = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            using (PluginExecutionCancellation.Create(caller.Token, revocation.Token))
            { }
        }
        var bothCancelablePerCall = (GC.GetAllocatedBytesForCurrentThread() - beforeBoth) / Iterations;

        // The common path allocates no linked CTS; coverage instrumentation can add tiny loop
        // bookkeeping, so keep this below any plausible linked-source allocation floor.
        Assert.True(
            nonCancelablePerCall < 16L,
            $"non-cancelable path should be allocation-free apart from instrumentation overhead: " +
            $"{nonCancelablePerCall} bytes/call.");
        // The both-cancelable path is the only one that allocates a linked CancellationTokenSource
        // (well over this floor on a 64-bit runtime), so the saved allocation is unmistakable.
        Assert.True(
            bothCancelablePerCall - nonCancelablePerCall >= 64L,
            $"non-cancelable path must skip the linked CancellationTokenSource: " +
            $"nonCancelable={nonCancelablePerCall} bytes/call, bothCancelable={bothCancelablePerCall} bytes/call.");
    }

    [Fact]
    public void BothCancelableTokens_areLinkedSoEitherCancels()
    {
        using var caller = new CancellationTokenSource();
        using var revocation = new CancellationTokenSource();

        using var scope = PluginExecutionCancellation.Create(caller.Token, revocation.Token);

        // A linked token is distinct from both inputs and observes either source firing.
        Assert.NotEqual(caller.Token, scope.Token);
        Assert.NotEqual(revocation.Token, scope.Token);
        Assert.False(scope.Token.IsCancellationRequested);
        revocation.Cancel();
        Assert.True(scope.Token.IsCancellationRequested);
    }

    [Fact]
    public void NonCancelableRevocation_passesCallerTokenThrough()
    {
        using var caller = new CancellationTokenSource();

        // Revocation can never fire (default token), so the caller token alone carries cancellation
        // and no linked source is needed.
        using var scope = PluginExecutionCancellation.Create(caller.Token, CancellationToken.None);

        Assert.Equal(caller.Token, scope.Token);
    }
}
