namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted deep composition: three <c>Where</c> stages and three <c>Select</c> stages compose into a
/// single lowered chain (filters AND-compose, projections chain). The discriminating predicate is the second one,
/// reading a field of the first projection — proving every stage runs server-side before the push.
/// </summary>
public sealed class DeepCompositionTests
{
    [Fact]
    public async Task Three_filters_and_three_projections_compose_into_one_chain()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new { e.Zone, e.Score, e.Boss })
            .Where(x => x.Boss)
            .Select(x => new { x.Zone, Big = x.Score > 1_000_000_000L })
            .Where(x => x.Big)
            .Select(x => x.Zone)
            .RunLocal((zone, ctx) => received.Add(zone));

        await h.PublishAsync(SampleEvents.Matching);                       // Boss + Score 9e9 -> "crypt"
        await h.PublishAsync(SampleEvents.Matching with { Boss = false }); // filtered at the second Where

        Assert.Equal("crypt", Assert.Single(received));
    }
}
