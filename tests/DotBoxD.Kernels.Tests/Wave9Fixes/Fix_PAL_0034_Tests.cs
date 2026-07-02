using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Wave9Fixes;

/// <summary>
/// Regression coverage for PAL-0034: forwarding audit events to host observers must not
/// rebuild a multicast invocation-list array for every <see cref="SandboxAuditEvent"/>.
///
/// <para>
/// Today <c>SandboxHost.PublishToAuditObservers</c> calls
/// <c>_auditObserver!.GetInvocationList()</c> once per published audit event, so result
/// publication allocates a fresh <c>Delegate[]</c> array for each event even though the
/// observer set is fixed for the lifetime of the host. That makes observer dispatch cost
/// scale as O(event-count * observer-count) in array allocations.
/// </para>
///
/// <para>
/// The observer set is stable after host construction, so publication should reuse a single
/// snapshot of the observers instead of materializing the invocation list per event. This test
/// pins that steady-state behavior: holding the execution path (plan, input, options, and
/// therefore the produced audit-event count) byte-for-byte identical, the marginal per-run
/// allocation of adding many observers must stay bounded by roughly a single snapshot array,
/// NOT grow by one invocation-list array per audit event.
/// </para>
///
/// <para>
/// It uses only public API: <see cref="SandboxHost"/>, <see cref="SandboxHostBuilder"/>,
/// <c>ForwardAuditEventsTo</c>, and <see cref="SandboxExecutionResult.AuditEvents"/>. It is RED
/// while the per-event <c>GetInvocationList()</c> allocation is present, because the many-observer
/// configuration allocates an extra <c>Delegate[]</c> per audit event on every run.
/// </para>
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class Fix_PAL_0034_Tests
{
    private const int ManyObservers = 64;
    private const int WarmupRuns = 32;
    private const int MeasuredRuns = 400;

    // Upper bound on the *marginal* per-run allocation attributable to registering
    // ManyObservers no-op observers instead of one.
    //
    // A snapshot / copy-on-write publish materializes the observer array once (at host build
    // or first publish) and reuses it for every event of every run, so the steady-state
    // marginal per-run array allocation is ~0 regardless of observer count.
    //
    // The current per-event GetInvocationList() path allocates a fresh Delegate[K] array
    // (24 + 8*K bytes) for EVERY published audit event. Per run that costs:
    //   many host (K=ManyObservers): event-count * (24 + 8*ManyObservers)
    //   single host (K=1)          : event-count * (24 + 8)
    // so the marginal cost is event-count * 8 * (ManyObservers - 1) bytes/run, which is at
    // least 8 * (ManyObservers - 1) bytes/run even for a single audit event. The threshold is
    // kept below that single-event lower bound so the buggy regime fails for any event count
    // >= 1, while leaving headroom for instrumentation overhead in coverage runs. Coverlet can
    // add a small marginal allocation to the many-observer path because it records many more
    // observer invocations; that is not the per-event invocation-list allocation this test guards.
    private const long SingleEventBuggyMarginalLowerBound = 8 * (ManyObservers - 1);
    private const long MaxMarginalBytesPerRun = SingleEventBuggyMarginalLowerBound * 3 / 4;

    [Fact]
    public async Task Observer_forwarding_preserves_per_event_delivery_to_every_observer()
    {
        // Behavioral guard: the optimization must keep delivering every audit event to every
        // registered observer (exception isolation and full fan-out are preserved).
        var first = new List<SandboxAuditEvent>();
        var second = new List<SandboxAuditEvent>();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.ForwardAuditEventsTo(first.Add);
            builder.ForwardAuditEventsTo(second.Add);
        });
        var plan = await PreparePlanAsync(host);

        var result = await RunOnceAsync(host, plan);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.NotEmpty(result.AuditEvents);
        Assert.Equal(result.AuditEvents, first);
        Assert.Equal(result.AuditEvents, second);
    }

    [Fact]
    public async Task Adding_many_stable_observers_does_not_allocate_an_invocation_list_per_event()
    {
        var singleObserverHost = BuildHostWithObservers(1);
        var manyObserverHost = BuildHostWithObservers(ManyObservers);

        var singlePlan = await PreparePlanAsync(singleObserverHost);
        var manyPlan = await PreparePlanAsync(manyObserverHost);

        // Confirm the execution path is identical: same audit-event count drives publication,
        // so any allocation difference comes from observer dispatch, not from execution.
        var probe = await RunOnceAsync(manyObserverHost, manyPlan);
        Assert.True(probe.Succeeded, probe.Error?.SafeMessage);
        var eventsPerRun = probe.AuditEvents.Count;
        Assert.True(eventsPerRun >= 1, "expected the run to produce at least one audit event");

        var singleCost = await PerRunAllocationAsync(singleObserverHost, singlePlan);
        var manyCost = await PerRunAllocationAsync(manyObserverHost, manyPlan);

        var marginalPerRun = manyCost - singleCost;

        Assert.True(
            marginalPerRun <= MaxMarginalBytesPerRun,
            $"Registering {ManyObservers} stable audit observers added ~{marginalPerRun} bytes/run " +
            $"over a single observer while producing {eventsPerRun} audit event(s) per run " +
            $"(allowed marginal: {MaxMarginalBytesPerRun} bytes/run). A stable observer set should " +
            "reuse one snapshot array; the per-event GetInvocationList() allocation instead rebuilds a " +
            "Delegate[] invocation list for every audit event, so the marginal cost grows with event count.");
    }

    private static SandboxHost BuildHostWithObservers(int observerCount)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            for (var i = 0; i < observerCount; i++)
            {
                builder.ForwardAuditEventsTo(NoOpObserver);
            }
        });

    private static async Task<ExecutionPlan> PreparePlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, BuildPolicy());
    }

    private static SandboxPolicy BuildPolicy()
        => SandboxPolicyBuilder.Create().WithFuel(1_000).Build();

    private static ValueTask<SandboxExecutionResult> RunOnceAsync(SandboxHost host, ExecutionPlan plan)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

    private static async Task<long> PerRunAllocationAsync(SandboxHost host, ExecutionPlan plan)
    {
        // Warm up: prime caches (compiled/hotness state is irrelevant for interpreted mode,
        // but JIT and any first-touch state stabilize here) before measuring steady state.
        for (var i = 0; i < WarmupRuns; i++)
        {
            await RunOnceAsync(host, plan);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredRuns; i++)
        {
            await RunOnceAsync(host, plan);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / MeasuredRuns;
    }

    private static void NoOpObserver(SandboxAuditEvent auditEvent)
    {
        // Intentionally empty: the observer must do no work so the only difference between the
        // single-observer and many-observer hosts is the dispatch/invocation-list allocation.
    }
}
