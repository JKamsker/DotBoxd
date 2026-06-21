namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted anonymous-object projections — both as the terminal pushed value (wired by a generic
/// interceptor whose type parameter Roslyn infers at the call site, decoded by a generated anonymous-object literal)
/// and as an intermediate server-side stage whose fields are read by a downstream <c>Where</c> before a named
/// terminal projection.
/// </summary>
public sealed class AnonymousProjectionTests
{
    [Fact]
    public async Task Anonymous_terminal_projection_round_trips_to_the_native_delegate()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new { Id = e.EncounterId, Zone = e.Zone })
            .RunLocal((x, ctx) => received.Add($"{x.Id}|{x.Zone}"));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal($"{SampleEvents.SampleId}|crypt", Assert.Single(received));
    }

    [Fact]
    public async Task Anonymous_intermediate_projection_filters_then_projects_a_named_terminal()
    {
        // Select(e => new { Id, Dist }) builds an anonymous record server-side; .Where(x => x.Dist <= 3) reads its
        // field via record.get; the terminal Select(x => x.Id) projects a NAMED Guid that is the pushed value.
        var received = new List<Guid>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new { Id = e.EncounterId, Dist = e.Distance })
            .Where(x => x.Dist <= 3)
            .Select(x => x.Id)
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(SampleEvents.Matching);                          // Dist 3 <= 3 -> matches
        await h.PublishAsync(SampleEvents.Matching with { Distance = 4 });    // leading Where passes, Dist 4 -> drop

        Assert.Equal(SampleEvents.SampleId, Assert.Single(received));
    }

    [Fact]
    public async Task Anonymous_intermediate_with_multiple_fields_filters_server_side()
    {
        // A wider anonymous tuple (string/long/bool) filtered on two of its fields, then a named terminal projection.
        var received = new List<string>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new { Zone = e.Zone, Score = e.Score, Boss = e.Boss })
            .Where(x => x.Score > 1_000_000_000L && x.Boss)
            .Select(x => x.Zone)
            .RunLocal((zone, ctx) => received.Add(zone));

        await h.PublishAsync(SampleEvents.Matching);                       // Score 9e9 > 1e9 && Boss -> matches
        await h.PublishAsync(SampleEvents.Matching with { Boss = false }); // Boss false -> filtered downstream

        Assert.Equal("crypt", Assert.Single(received));
    }
}
