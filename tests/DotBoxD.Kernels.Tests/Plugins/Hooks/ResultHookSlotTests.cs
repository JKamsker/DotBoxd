using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

/// <summary>
/// Result-hook dispatch semantics: descending-priority ordering, abstain/fallthrough, install-order tie-breaks,
/// first-success-wins, fault isolation, and cancellation — exercised against <see cref="ResultHookSlot{TEvent, TContext}"/>
/// directly with pure delegate handlers (no sandbox kernel needed).
/// </summary>
public sealed class ResultHookSlotTests
{
    private sealed record DamageCtx(int Damage);

    private readonly record struct TestResult(bool Success, string? Reason, int Value = 0) : IHookResult;

    private readonly record struct OtherResult(bool Success, string? Reason) : IHookResult;

    private static ResultHookSlot<DamageCtx, HookContext> NewSlot(Action<ResultHookFault>? onFault = null)
        => new(new StubAdapter(), onFault);

    private static HookContext Context() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    private static ValueTask<IHookResult?> Ok(int value) => new((IHookResult?)new TestResult(true, null, value));

    private static ValueTask<IHookResult?> Abstain() => new((IHookResult?)new TestResult(false, "abstain"));

    private static ValueTask<IHookResult?> FilterMiss() => new((IHookResult?)null);

    [Fact]
    public async Task No_handlers_returns_null()
    {
        var slot = NewSlot();

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Single_successful_handler_wins()
    {
        var slot = NewSlot();
        slot.AddDirect(0, (_, _, _) => Ok(42));

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Equal(42, result!.Value.Value);
    }

    [Fact]
    public async Task Filter_miss_does_not_produce_a_result()
    {
        var slot = NewSlot();
        slot.AddDirect(0, (_, _, _) => FilterMiss());

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Abstain_falls_through_to_the_next_successful_handler()
    {
        var slot = NewSlot();
        slot.AddDirect(100, (_, _, _) => Abstain());
        slot.AddDirect(0, (_, _, _) => Ok(7));

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Equal(7, result!.Value.Value);
    }

    [Fact]
    public async Task Higher_priority_result_wins()
    {
        var slot = NewSlot();
        slot.AddDirect(0, (_, _, _) => Ok(1));
        slot.AddDirect(100, (_, _, _) => Ok(2));

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Equal(2, result!.Value.Value);
    }

    [Fact]
    public async Task Same_priority_preserves_install_order()
    {
        var slot = NewSlot();
        slot.AddDirect(5, (_, _, _) => Ok(1));
        slot.AddDirect(5, (_, _, _) => Ok(2));

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Equal(1, result!.Value.Value);
    }

    [Fact]
    public async Task Faulty_handler_is_isolated_reported_and_dispatch_continues()
    {
        var faults = new List<ResultHookFault>();
        var slot = NewSlot(faults.Add);
        var boom = new InvalidOperationException("boom");
        slot.AddDirect(100, (_, _, _) => throw boom);
        slot.AddDirect(0, (_, _, _) => Ok(9));

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Equal(9, result!.Value.Value);
        var fault = Assert.Single(faults);
        Assert.Same(boom, fault.Exception);
        Assert.Equal(typeof(DamageCtx), fault.EventType);
    }

    [Fact]
    public async Task Throwing_veto_handler_is_reported_and_fails_open()
    {
        // A veto-bearing handler (a successful result carrying a domain veto) that throws must be reported rather
        // than silently swallowed; with no other handler dispatch returns null and the host applies its default.
        var faults = new List<ResultHookFault>();
        var slot = NewSlot(faults.Add);
        slot.AddDirect(0, (_, _, _) => throw new InvalidOperationException("veto blew up"));

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Null(result);
        Assert.Single(faults);
    }

    [Fact]
    public async Task A_throwing_fault_observer_does_not_abort_dispatch()
    {
        // The fault channel must never escalate: a misbehaving observer is swallowed and dispatch still falls
        // through to the next successful handler.
        var slot = NewSlot(_ => throw new InvalidOperationException("observer blew up"));
        slot.AddDirect(100, (_, _, _) => throw new InvalidOperationException("handler boom"));
        slot.AddDirect(0, (_, _, _) => Ok(3));

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Equal(3, result!.Value.Value);
    }

    [Fact]
    public async Task Cancellation_stops_dispatch()
    {
        var slot = NewSlot();
        var invoked = false;
        slot.AddDirect(0, (_, _, _) =>
        {
            invoked = true;
            return Ok(1);
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
            {
                var context = Context();
                await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, cts.Token);
            });
        Assert.False(invoked);
    }

    [Fact]
    public async Task Remote_timeout_returns_configured_fail_closed_result_and_reports_fault()
    {
        var faults = new List<ResultHookFault>();
        var slot = NewSlot(faults.Add);
        slot.AddDirect(
            0,
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                return new TestResult(true, null, 999);
            },
            remote: true);
        var options = ResultHookDispatchOptions<TestResult>.FailClosedAfter(
            TimeSpan.FromMilliseconds(100),
            new TestResult(true, "timeout", -1));

        var context = Context();
        var result = await slot.FireAsync(
            new DamageCtx(10),
            context,
            context,
            options,
            CancellationToken.None);

        Assert.Equal(-1, result!.Value.Value);
        Assert.IsType<TimeoutException>(Assert.Single(faults).Exception);
    }

    [Fact]
    public void Default_remote_timeout_is_finite()
    {
        Assert.NotEqual(
            Timeout.InfiniteTimeSpan,
            ResultHookDispatchOptions<TestResult>.Default.RemoteHandlerTimeout);
    }

    [Fact]
    public async Task Infinite_remote_timeout_is_explicit_opt_in()
    {
        var slot = NewSlot();
        slot.AddDirect(
            0,
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
                return new TestResult(true, null, 5);
            },
            remote: true);
        var options = new ResultHookDispatchOptions<TestResult>
        {
            RemoteHandlerTimeout = Timeout.InfiniteTimeSpan
        };

        var context = Context();
        var result = await slot.FireAsync(new DamageCtx(10), context, context, options, CancellationToken.None);

        Assert.Equal(5, result!.Value.Value);
    }

    [Fact]
    public async Task Wrong_result_type_is_reported_and_dispatch_continues()
    {
        var faults = new List<ResultHookFault>();
        var slot = NewSlot(faults.Add);
        slot.AddDirect(100, (_, _, _) => new ValueTask<IHookResult?>((IHookResult)new OtherResult(true, null)));
        slot.AddDirect(0, (_, _, _) => Ok(4));

        var context = Context();
        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), context, context, CancellationToken.None);

        Assert.Equal(4, result!.Value.Value);
        Assert.IsType<InvalidCastException>(Assert.Single(faults).Exception);
    }

    [Fact]
    public async Task Oversized_remote_timeout_is_rejected_before_dispatch()
    {
        var slot = NewSlot();
        slot.AddDirect(0, (_, _, _) => Ok(1), remote: true);
        var options = new ResultHookDispatchOptions<TestResult>
        {
            RemoteHandlerTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1)
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () =>
            {
                var context = Context();
                await slot.FireAsync(new DamageCtx(10), context, context, options, CancellationToken.None);
            });
    }

    private sealed class StubAdapter : IPluginEventAdapter<DamageCtx>
    {
        public string EventName => "test.damage";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageCtx e) => [];
    }
}
