namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted filter-project-filter chains: a downstream <c>Where</c> that reads a field of the
/// <em>projected</em> value (via <c>record.get</c>) rather than the event. Proves the projected field is readable
/// for a constructed <c>new Dto(...)</c> and an existing-record projection, that the downstream filter still runs
/// server-side, and — critically — that a projected field name colliding with an event property resolves to the
/// projected field, not the event property.
/// </summary>
public sealed class FilterProjectFilterTests
{
    [Fact]
    public async Task Downstream_filter_reads_a_projected_record_field()
    {
        // .Select(e => new RangedTicket(e.Distance, e.Zone)).Where(t => t.Range >= 2): both events clear the leading
        // Where (Distance <= 4); the downstream filter on the projected Range is the discriminator, server-side.
        var received = new List<RangedTicket>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new RangedTicket(e.Distance, e.Zone))
            .Where(t => t.Range >= 2)
            .RunLocal((t, ctx) => received.Add(t));

        await h.PublishAsync(SampleEvents.Matching with { Distance = 3 });   // Range 3 >= 2 -> matches
        await h.PublishAsync(SampleEvents.Matching with { Distance = 1 });   // Range 1 >= 2 -> filtered downstream

        Assert.Equal(new RangedTicket(3, "crypt"), Assert.Single(received));
    }

    [Fact]
    public async Task Downstream_filter_reads_the_projected_field_not_a_colliding_event_property()
    {
        // The projected NearTicket.Near holds e.Far, while DualEvent also has its own Near with a DIFFERENT value.
        // The downstream Where(t => t.Near <= 4) must read the PROJECTED Near (== Far), not the event's Near.
        //   matching:  Near=99, Far=3  -> projected Near 3  <= 4 -> pushed   (event-property read would see 99 -> drop)
        //   filtered:  Near=0,  Far=10 -> projected Near 10 >  4 -> filtered (event-property read would see 0  -> push)
        var received = new List<NearTicket>();
        using var h = new RunLocalHarness<DualEvent>();

        h.Hooks.On<DualEvent>()
            .Where(e => e.Far <= 50)
            .Select(e => new NearTicket(e.Far))
            .Where(t => t.Near <= 4)
            .RunLocal((t, ctx) => received.Add(t));

        await h.PublishAsync(new DualEvent(Near: 99, Far: 3));
        await h.PublishAsync(new DualEvent(Near: 0, Far: 10));

        Assert.Equal(new NearTicket(3), Assert.Single(received));
    }

    [Fact]
    public async Task Downstream_filter_reads_a_field_of_an_existing_projected_record()
    {
        // .Select(e => e.Player).Where(p => p.Level >= 5): the projected value is an existing record (PlayerInfo),
        // and the downstream filter reads its Level field. Both events clear the leading Where; Level discriminates.
        var received = new List<PlayerInfo>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Player)
            .Where(p => p.Level >= 5)
            .RunLocal((p, ctx) => received.Add(p));

        await h.PublishAsync(SampleEvents.Matching with { Player = new PlayerInfo("hero", 7) });   // Level 7 -> push
        await h.PublishAsync(SampleEvents.Matching with { Player = new PlayerInfo("squire", 1) }); // Level 1 -> drop

        Assert.Equal(new PlayerInfo("hero", 7), Assert.Single(received));
    }
}
