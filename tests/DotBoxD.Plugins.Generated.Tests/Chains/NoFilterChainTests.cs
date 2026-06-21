namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted chains with NO leading <c>Where</c>: every published event is projected/pushed. Proves a
/// filter is optional in the lowered chain — both a scalar projection and a whole-event push deliver every event.
/// </summary>
public sealed class NoFilterChainTests
{
    [Fact]
    public async Task Scalar_projection_without_a_filter_delivers_every_event()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Select(e => e.MonsterId)
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal(new[] { "m-1", "m-2" }, received);
    }

    [Fact]
    public async Task Whole_event_without_a_filter_delivers_every_event()
    {
        var received = new List<ChainAggroEvent>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .RunLocal((e, ctx) => received.Add(e));

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal(
            new[] { new ChainAggroEvent("m-1", 3), new ChainAggroEvent("m-2", 99) },
            received);
    }
}
