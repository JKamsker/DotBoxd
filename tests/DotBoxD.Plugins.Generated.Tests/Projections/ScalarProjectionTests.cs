namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted scalar projections: each chain's terminal <c>Select</c> projects a single scalar (string,
/// int, long, double, bool, Guid, enum) server-side, and the native <c>RunLocal</c> delegate receives exactly that
/// scalar. Every test also publishes a non-matching event to prove the leading <c>Where</c> ran server-side, so
/// only the matching projected scalar crosses the wire. The DotBoxD generator intercepts each <c>RunLocal</c> call
/// site at build time (see the .csproj); an un-intercepted chain would throw at the terminal.
/// </summary>
public sealed class ScalarProjectionTests
{
    [Fact]
    public async Task String_projection_pushes_only_the_matching_value()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));    // 3 <= 4 -> projected + pushed
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));   // 99 > 4 -> filtered server-side

        Assert.Equal("m-1", Assert.Single(received));
    }

    [Fact]
    public async Task Int_projection_round_trips_the_scalar()
    {
        var received = new List<int>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Distance)
            .RunLocal((distance, ctx) => received.Add(distance));

        await h.PublishAsync(new ChainAggroEvent("m-1", 2));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal(2, Assert.Single(received));
    }

    [Fact]
    public async Task Guid_projection_round_trips_the_scalar()
    {
        var received = new List<Guid>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.EncounterId)
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(SampleEvents.SampleId, Assert.Single(received));
    }

    [Fact]
    public async Task Enum_projection_round_trips_a_non_zero_value()
    {
        var received = new List<GamePhase>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Phase)
            .RunLocal((phase, ctx) => received.Add(phase));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(GamePhase.Victory, Assert.Single(received));
    }

    [Fact]
    public async Task Long_projection_above_int_range_round_trips()
    {
        var received = new List<long>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Score)
            .RunLocal((score, ctx) => received.Add(score));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(9_000_000_000L, Assert.Single(received));
    }

    [Fact]
    public async Task Double_projection_round_trips()
    {
        var received = new List<double>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Multiplier)
            .RunLocal((multiplier, ctx) => received.Add(multiplier));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(1.25, Assert.Single(received));
    }

    [Fact]
    public async Task Bool_projection_round_trips()
    {
        var received = new List<bool>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Boss)
            .RunLocal((boss, ctx) => received.Add(boss));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.True(Assert.Single(received));
    }
}
