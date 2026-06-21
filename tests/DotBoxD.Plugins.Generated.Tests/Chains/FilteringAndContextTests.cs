namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted coverage for the server-side filtering premise and the native terminal's runtime context:
/// a non-matching event never reaches the delegate, multiple leading <c>Where</c> clauses AND-compose server-side,
/// the <c>RunLocal</c> body can use its <see cref="DotBoxD.Abstractions.HookContext"/> to send a host message, and
/// two independent chains on the same event type are intercepted and wired independently.
/// </summary>
public sealed class FilteringAndContextTests
{
    [Fact]
    public async Task A_non_matching_event_never_reaches_the_native_delegate()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(new ChainAggroEvent("m-far", 99));   // 99 > 4 -> filtered server-side, no push

        Assert.Empty(received);
    }

    [Fact]
    public async Task Multiple_leading_where_clauses_and_compose_server_side()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Where(e => e.Distance >= 2)
            .Select(e => e.MonsterId)
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(new ChainAggroEvent("ok", 3));    // 2 <= 3 <= 4 -> matches
        await h.PublishAsync(new ChainAggroEvent("lo", 1));    // 1 < 2       -> filtered
        await h.PublishAsync(new ChainAggroEvent("hi", 99));   // 99 > 4      -> filtered

        Assert.Equal("ok", Assert.Single(received));
    }

    [Fact]
    public async Task RunLocal_body_can_send_a_host_message_through_its_context()
    {
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal((id, ctx) => ctx.Messages.Send(id, "calm"));

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        var message = Assert.Single(h.Sink.Messages);
        Assert.Equal("m-1", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    [Fact]
    public async Task Two_independent_chains_on_the_same_event_type_are_wired_independently()
    {
        var projected = new List<string>();
        var whole = new List<ChainAggroEvent>();
        using var projectedHarness = new RunLocalHarness<ChainAggroEvent>();
        using var wholeHarness = new RunLocalHarness<ChainAggroEvent>();

        projectedHarness.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal((id, ctx) => projected.Add(id));

        wholeHarness.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 10)
            .RunLocal((e, ctx) => whole.Add(e));

        await projectedHarness.PublishAsync(new ChainAggroEvent("m-1", 3));
        await wholeHarness.PublishAsync(new ChainAggroEvent("m-2", 7));

        Assert.Equal("m-1", Assert.Single(projected));
        Assert.Equal(new ChainAggroEvent("m-2", 7), Assert.Single(whole));
    }
}
