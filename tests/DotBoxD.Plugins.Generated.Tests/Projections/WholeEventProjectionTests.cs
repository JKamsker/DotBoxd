using System.Numerics;

namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted whole-event chains: a <c>Where</c> with no <c>Select</c> pushes the entire event record
/// across the wire, and the native <c>RunLocal</c> delegate receives the reconstructed event. These prove the
/// value-writer field order matches the marshaller's record reconstruction across rich field kinds — Guid, enum,
/// arrays, nested DTOs, floats/field-structs, ulong enums above <c>long.MaxValue</c>, and a non-positional event
/// whose constructor order differs from its declaration order.
/// </summary>
public sealed class WholeEventProjectionTests
{
    [Fact]
    public async Task Flat_event_round_trips_as_the_whole_record()
    {
        var received = new List<ChainAggroEvent>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .RunLocal((e, ctx) => received.Add(e));

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal(new ChainAggroEvent("m-1", 3), Assert.Single(received));
    }

    [Fact]
    public async Task Rich_event_round_trips_every_field()
    {
        var received = new List<EncounterEvent>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .RunLocal((e, ctx) => received.Add(e));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        var got = Assert.Single(received);
        Assert.Equal(SampleEvents.SampleId, got.EncounterId);
        Assert.Equal(GamePhase.Victory, got.Phase);
        Assert.True(got.Boss);
        Assert.Equal(3, got.Distance);
        Assert.Equal(9_000_000_000L, got.Score);
        Assert.Equal(1.25, got.Multiplier);
        Assert.Equal("crypt", got.Zone);
        Assert.Equal(new[] { 3, 1, 4, 1, 5 }, got.MonsterIds);
        Assert.Equal(new PlayerInfo("hero", 7), got.Player);
    }

    [Fact]
    public async Task Float_and_public_field_struct_event_round_trips_with_field_fidelity()
    {
        // All float literals are exactly representable, so the float -> F64 -> float widen/narrow is lossless.
        var received = new List<FieldStructEvent>();
        using var h = new RunLocalHarness<FieldStructEvent>();

        h.Hooks.On<FieldStructEvent>()
            .Where(e => e.Distance <= 4)
            .RunLocal((e, ctx) => received.Add(e));

        var matching = new FieldStructEvent(
            SampleEvents.SampleId,
            Distance: 3,
            Health: 0.5f,
            Velocity: new Vector3(1.5f, -2.25f, 3f),
            Spot: new MapPoint(42, new Vector3(10f, 20f, 30f)),
            Zone: "crypt");

        await h.PublishAsync(matching);
        await h.PublishAsync(matching with { Distance = 99 });

        var got = Assert.Single(received);
        Assert.Equal(SampleEvents.SampleId, got.Id);
        Assert.Equal(0.5f, got.Health);
        Assert.Equal(new Vector3(1.5f, -2.25f, 3f), got.Velocity);
        Assert.Equal(new MapPoint(42, new Vector3(10f, 20f, 30f)), got.Spot);
        Assert.Equal("crypt", got.Zone);
    }

    [Fact]
    public async Task Non_positional_event_round_trips_in_declaration_order()
    {
        // SwappedEvent declares [Distance, Id, Zone] but its constructor is (id, zone, distance). The distinct
        // field types (int/Guid/string) mean any transposition would throw a kind-mismatch, not corrupt silently.
        var received = new List<SwappedEvent>();
        using var h = new RunLocalHarness<SwappedEvent>();

        h.Hooks.On<SwappedEvent>()
            .Where(e => e.Distance <= 4)
            .RunLocal((e, ctx) => received.Add(e));

        var id = new Guid("11112222-3333-4444-5555-666677778888");
        await h.PublishAsync(new SwappedEvent(id, "crypt", 3));
        await h.PublishAsync(new SwappedEvent(id, "crypt", 99));

        var got = Assert.Single(received);
        Assert.Equal(id, got.Id);
        Assert.Equal("crypt", got.Zone);
        Assert.Equal(3, got.Distance);
    }

    [Fact]
    public async Task Ulong_enum_above_long_max_round_trips_in_a_whole_event()
    {
        var received = new List<HugeEnumEvent>();
        using var h = new RunLocalHarness<HugeEnumEvent>();

        h.Hooks.On<HugeEnumEvent>()
            .Where(e => e.Distance <= 4)
            .RunLocal((e, ctx) => received.Add(e));

        await h.PublishAsync(new HugeEnumEvent(3, HugeEnum.Top));
        await h.PublishAsync(new HugeEnumEvent(99, HugeEnum.Top));

        var got = Assert.Single(received);
        Assert.Equal(HugeEnum.Top, got.Big);
        Assert.Equal(3, got.Distance);
    }
}
