namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted RunLocal chains on the SUBSCRIPTION surface (<c>RemoteSubscriptionRegistry</c> /
/// <c>server.Subscriptions</c>) — a distinct interception path from the hooks surface. Subscription publish is
/// fire-and-forget, so each test gates on the matching event's delivery and asserts only it crossed (the
/// non-matching event is filtered server-side and never signals).
/// </summary>
public sealed class SubscriptionProjectionTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Scalar_projection_is_intercepted_on_the_subscription_surface()
    {
        var received = new List<string>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var h = new SubscriptionHarness<ChainAggroEvent>();

        h.Subscriptions.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal((id, ctx) =>
            {
                received.Add(id);
                delivered.TrySetResult();
            });

        h.Publish(new ChainAggroEvent("m-2", 99));   // filtered server-side -> never signals
        h.Publish(new ChainAggroEvent("m-1", 3));    // matches -> signals delivery

        await delivered.Task.WaitAsync(DeliveryTimeout);
        Assert.Equal("m-1", Assert.Single(received));
    }

    [Fact]
    public async Task Whole_event_is_intercepted_on_the_subscription_surface()
    {
        var received = new List<ChainAggroEvent>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var h = new SubscriptionHarness<ChainAggroEvent>();

        h.Subscriptions.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .RunLocal((e, ctx) =>
            {
                received.Add(e);
                delivered.TrySetResult();
            });

        h.Publish(new ChainAggroEvent("m-2", 99));
        h.Publish(new ChainAggroEvent("m-1", 3));

        await delivered.Task.WaitAsync(DeliveryTimeout);
        Assert.Equal(new ChainAggroEvent("m-1", 3), Assert.Single(received));
    }

    [Fact]
    public async Task Dto_projection_is_intercepted_on_the_subscription_surface()
    {
        var received = new List<EncounterTicket>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var h = new SubscriptionHarness<EncounterEvent>();

        h.Subscriptions.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new EncounterTicket(e.EncounterId, e.Zone))
            .RunLocal((ticket, ctx) =>
            {
                received.Add(ticket);
                delivered.TrySetResult();
            });

        h.Publish(SampleEvents.Filtered);
        h.Publish(SampleEvents.Matching);

        await delivered.Task.WaitAsync(DeliveryTimeout);
        Assert.Equal(new EncounterTicket(SampleEvents.SampleId, "crypt"), Assert.Single(received));
    }
}
