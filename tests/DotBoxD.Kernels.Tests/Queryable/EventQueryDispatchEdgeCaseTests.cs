using DotBoxD.Abstractions;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

/// <summary>Value-shape edge cases for indexed dispatch: null/numeric/unsigned/signed-zero routing and faulty getters.</summary>
public sealed class EventQueryDispatchEdgeCaseTests
{
    private static HookContext NewContext() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    [Fact]
    public async Task Null_equality_subscription_matches_events_with_a_null_member()
    {
        var host = new EventQueryHost();
        var matched = new List<NullableTestEvent>();

        var handle = await host.Query<NullableTestEvent>()
            .Where(e => e.Key == null)
            .SubscribeAsync((e, _) =>
            {
                matched.Add(e);
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        await host.PublishAsync(new NullableTestEvent(null, 1), context);
        await host.PublishAsync(new NullableTestEvent("set", 2), context);

        Assert.Single(matched);
        Assert.Null(matched[0].Key);
        // Null equality is not index-routable; it must fall back to broad evaluation.
        Assert.False(handle.Plan.IsRoutable);
        Assert.Equal(2, handle.FilterEvaluations);
    }

    [Fact]
    public async Task Numeric_equality_routes_across_integer_literal_and_floating_member()
    {
        var host = new EventQueryHost();
        var matched = new List<MetricTestEvent>();

        // Integer literal compared against a double member: the captured value is Integer while the runtime
        // member reads as a double — the routing key must still match.
        var handle = await host.Query<MetricTestEvent>()
            .Where(e => e.Score == 100)
            .SubscribeAsync((e, _) =>
            {
                matched.Add(e);
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        await host.PublishAsync(new MetricTestEvent("a", 100.0), context);
        await host.PublishAsync(new MetricTestEvent("b", 99.5), context);

        Assert.Single(matched);
        Assert.Equal("a", matched[0].Id);
        Assert.True(handle.Plan.IsRoutable);
    }

    [Fact]
    public async Task Large_unsigned_equality_matches_its_own_value()
    {
        var host = new EventQueryHost();
        var matched = 0;

        await host.Query<UnsignedTestEvent>()
            .Where(e => e.Big == ulong.MaxValue)
            .SubscribeAsync((_, _) =>
            {
                matched++;
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        await host.PublishAsync(new UnsignedTestEvent(ulong.MaxValue), context);
        await host.PublishAsync(new UnsignedTestEvent(1), context);

        Assert.Equal(1, matched);
    }

    [Fact]
    public async Task Negative_zero_member_routes_to_a_zero_equality()
    {
        var host = new EventQueryHost();
        var matched = 0;

        await host.Query<MetricTestEvent>()
            .Where(e => e.Score == 0.0)
            .SubscribeAsync((_, _) =>
            {
                matched++;
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        await host.PublishAsync(new MetricTestEvent("a", -0.0), context);

        Assert.Equal(1, matched);
    }

    [Fact]
    public async Task Throwing_getter_does_not_abort_dispatch_for_other_subscriptions()
    {
        var host = new EventQueryHost();
        var idHits = 0;

        await host.Query<ThrowingGetterEvent>()
            .Where(e => e.Boom == "never")
            .SubscribeAsync((_, _) => ValueTask.CompletedTask);
        await host.Query<ThrowingGetterEvent>()
            .Where(e => e.Id == "x")
            .SubscribeAsync((_, _) =>
            {
                idHits++;
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        // Must not throw even though the first subscription's filter reads a getter that throws.
        await host.PublishAsync(new ThrowingGetterEvent("x"), context);

        Assert.Equal(1, idHits);
    }
}
