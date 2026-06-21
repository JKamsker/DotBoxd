namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>A projected DTO whose field name (<c>Range</c>) does NOT collide with any event property.</summary>
public sealed record RangedTicket(int Range, string Zone);

/// <summary>
/// An event with two same-typed scalar fields, used to make a projected-field-vs-event-property collision
/// observable: a projection remaps <see cref="Far"/> into a slot named <c>Near</c>, so reading the projected
/// <c>Near</c> (record.get) yields a different value than the same-named event property would.
/// </summary>
public sealed record DualEvent(int Near, int Far);

/// <summary>A DTO with a single field named <c>Near</c> that collides with <see cref="DualEvent.Near"/>.</summary>
public sealed record NearTicket(int Near);

/// <summary>
/// Filter-project-filter coverage: a downstream <c>Where</c> that reads a field of the <em>projected</em> value
/// (via a <c>record.get</c> on the projected record) rather than the event. Proves the projected field is readable
/// for both a constructed <c>new Dto(...)</c> and an existing-record projection, that the downstream filter still
/// runs server-side, and — critically — that a projected field name colliding with an event property resolves to
/// the projected field, not the event property (the silent-collision hole this closes).
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string FilterProjectFilterSource = Prelude + """
        public static class FilterProjectFilterUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new Ev.RangedTicket(e.Distance, e.Zone))
                    .Where(t => t.Range >= 2)
                    .RunLocal((t, ctx) => { });
        }
        """;

    private const string CollidingFieldFilterSource = Prelude + """
        public static class CollidingFieldFilterUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.DualEvent>().Where(e => e.Far <= 50)
                    .Select(e => new Ev.NearTicket(e.Far))
                    .Where(t => t.Near <= 4)
                    .RunLocal((t, ctx) => { });
        }
        """;

    private const string ExistingDtoFilterSource = Prelude + """
        public static class ExistingDtoFilterUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => e.Player)
                    .Where(p => p.Level >= 5)
                    .RunLocal((p, ctx) => { });
        }
        """;

    [Fact]
    public async Task Downstream_filter_reads_a_projected_record_field()
    {
        // .Select(e => new RangedTicket(e.Distance, e.Zone)).Where(t => t.Range >= 2): the downstream Where reads
        // RangedTicket.Range (record.get index 0). Both events clear the leading Where (Distance <= 4); the
        // DOWNSTREAM filter on the projected Range is the discriminator, so it must run server-side too.
        var payload = await PushFirstMatching(
            FilterProjectFilterSource,
            Matching with { Distance = 3 },   // Range 3 >= 2 -> matches
            Matching with { Distance = 1 });  // Range 1 >= 2 -> filtered downstream

        var expected = new RangedTicket(3, "crypt");
        Assert.Equal(expected, DecodeReflective<RangedTicket>(payload));
        Assert.Equal(expected, DecodeGenerated<RangedTicket>(FilterProjectFilterSource, payload));
    }

    [Fact]
    public async Task Downstream_filter_reads_the_projected_field_not_a_colliding_event_property()
    {
        // The projected NearTicket.Near holds e.Far, while DualEvent also has its own Near property with a DIFFERENT
        // value. The downstream Where(t => t.Near <= 4) must read the PROJECTED Near (== Far), not the event's Near.
        //   matching:  Near=99, Far=3  -> projected Near 3  <= 4 -> pushed   (event-property read would see 99 -> drop)
        //   filtered:  Near=0,  Far=10 -> projected Near 10 > 4 -> filtered  (event-property read would see 0  -> push)
        // So if the collision resolved to the event property, the WRONG event would cross and Near would not be 3.
        var payload = await PushFirstMatching(
            CollidingFieldFilterSource,
            new DualEvent(Near: 99, Far: 3),
            new DualEvent(Near: 0, Far: 10));

        var expected = new NearTicket(3);
        Assert.Equal(expected, DecodeReflective<NearTicket>(payload));
        Assert.Equal(expected, DecodeGenerated<NearTicket>(CollidingFieldFilterSource, payload));
    }

    [Fact]
    public async Task Downstream_filter_reads_a_field_of_an_existing_projected_record()
    {
        // .Select(e => e.Player).Where(p => p.Level >= 5): the projected value is an existing record (PlayerInfo),
        // and the downstream filter reads its Level field (record.get index 1). Both events clear the leading Where;
        // the projected Level discriminates.
        var payload = await PushFirstMatching(
            ExistingDtoFilterSource,
            Matching with { Player = new PlayerInfo("hero", 7) },   // Level 7 >= 5 -> matches
            Matching with { Player = new PlayerInfo("squire", 1) });// Level 1 >= 5 -> filtered downstream

        var expected = new PlayerInfo("hero", 7);
        Assert.Equal(expected, DecodeReflective<PlayerInfo>(payload));
        Assert.Equal(expected, DecodeGenerated<PlayerInfo>(ExistingDtoFilterSource, payload));
    }
}
