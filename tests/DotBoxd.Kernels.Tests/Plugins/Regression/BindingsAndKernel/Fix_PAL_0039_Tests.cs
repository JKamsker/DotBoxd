namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0039: binding dispatch creates a wall-time
/// cancellation source per host call. Both interpreted and compiled binding
/// dispatch call <see cref="SandboxContext.CreateWallTimeToken"/> for every
/// invocation. When the run cancellation token is the default
/// (non-cancelable) token, the wall-time deadline is already tracked by the
/// <see cref="ResourceMeter"/>, so the fast path should not allocate a fresh
/// linked <see cref="CancellationTokenSource"/> (plus its armed timer state)
/// per call.
/// </summary>
public sealed class Fix_PAL_0039_Tests
{
    // A generous wall-time budget so the produced token is never cancelled
    // mid-test and the deadline is the only reason a source would exist.
    private static SandboxContext Context()
    {
        var limits = new ResourceLimits(
            MaxFuel: 1_000_000,
            MaxAllocatedBytes: 1_000_000,
            MaxWallTime: TimeSpan.FromHours(1));

        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    [Fact]
    public void Wall_time_token_for_non_cancelable_run_does_not_allocate_per_call()
    {
        var context = Context();

        // Warm up first-call JIT / one-time allocations so the measured loop
        // only reflects per-call dispatch cost.
        DisposeIfOwned(context.CreateWallTimeToken());

        const int iterations = 1_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            DisposeIfOwned(context.CreateWallTimeToken());
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // RED until PAL-0039 is fixed: today every call links a fresh
        // CancellationTokenSource and arms a CancelAfter timer, so the fast
        // path allocates hundreds of bytes per invocation. A non-cancelable
        // run with the deadline tracked by the ResourceMeter must not allocate
        // a per-call cancellation source.
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Wall_time_token_for_non_cancelable_run_reuses_a_shared_source()
    {
        var context = Context();

        var first = context.CreateWallTimeToken();
        var second = context.CreateWallTimeToken();

        try
        {
            // RED until PAL-0039 is fixed: CreateLinkedTokenSource currently
            // returns a brand-new instance every call. On the fast path (a
            // non-cancelable run token) there is no asynchronous cancellation
            // to link, so the same wall-time source should be reused across
            // calls instead of allocating a distinct one each time.
            Assert.Same(first, second);
        }
        finally
        {
            DisposeIfOwned(first);
            if (!ReferenceEquals(first, second))
            {
                DisposeIfOwned(second);
            }
        }
    }

    private static void DisposeIfOwned(CancellationTokenSource? source) => source?.Dispose();
}
