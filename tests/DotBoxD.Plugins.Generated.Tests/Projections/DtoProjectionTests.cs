namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted record/DTO projections: projecting an existing nested record field, constructing a
/// <c>new Dto(...)</c>, constructing a record whose first field is itself a record (record-in-record), and a
/// <c>new Dto(..., EnumConstant)</c>. Each projected record round-trips to the native <c>RunLocal</c> delegate with
/// field-level fidelity. DTO construction also fails closed when a stored field would otherwise be silently
/// substituted with a manifest zero instead of the C# constructor/property default.
/// </summary>
public sealed class DtoProjectionTests
{
    [Fact]
    public async Task Existing_nested_record_projection_round_trips_its_fields()
    {
        var received = new List<PlayerInfo>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Player)
            .RunLocal((player, ctx) => received.Add(player));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(new PlayerInfo("hero", 7), Assert.Single(received));
    }

    [Fact]
    public async Task Constructed_new_dto_projection_round_trips_with_field_fidelity()
    {
        var received = new List<EncounterTicket>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new EncounterTicket(e.EncounterId, e.Zone))
            .RunLocal((ticket, ctx) => received.Add(ticket));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(new EncounterTicket(SampleEvents.SampleId, "crypt"), Assert.Single(received));
    }

    [Fact]
    public async Task Constructed_nested_dto_projection_round_trips_the_inner_record()
    {
        // new Squad(e.Player, e.Zone): the projected record's first field is itself a record (PlayerInfo), so
        // record.new nests a record value — the inner DTO's fields must survive the round-trip too.
        var received = new List<Squad>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new Squad(e.Player, e.Zone))
            .RunLocal((squad, ctx) => received.Add(squad));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(new Squad(new PlayerInfo("hero", 7), "crypt"), Assert.Single(received));
    }

    [Fact]
    public async Task New_dto_with_an_enum_constant_argument_round_trips()
    {
        // An enum CONSTANT argument lowers to its underlying I32 literal, matching the DTO's enum field, and
        // round-trips back to the enum value.
        var received = new List<PhaseTicket>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new PhaseTicket(e.EncounterId, GamePhase.Battle))
            .RunLocal((ticket, ctx) => received.Add(ticket));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(new PhaseTicket(SampleEvents.SampleId, GamePhase.Battle), Assert.Single(received));
    }

    [Fact]
    public void Constructed_dto_with_omitted_stored_member_is_not_intercepted()
    {
        using var h = new RunLocalHarness<EncounterEvent>();

        Assert.Throws<NotSupportedException>(() =>
        {
            h.Hooks.On<EncounterEvent>()
                .Where(e => e.Distance <= 4)
                .Select(e => new DefaultedProjectionTicket(e.EncounterId))
                .RunLocal((ticket, ctx) => { });
        });
    }
}

public sealed class DefaultedProjectionTicket
{
    public Guid EncounterId { get; set; }

    public string Zone { get; set; } = "fallback";

    public DefaultedProjectionTicket()
    {
    }

    public DefaultedProjectionTicket(Guid encounterId)
    {
        EncounterId = encounterId;
    }
}
