namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted map projection: a <c>Dictionary&lt;string, int&gt;</c> (scalar keys) projected server-side
/// round-trips to the native <c>RunLocal</c> delegate with its entries intact.
/// </summary>
public sealed class MapProjectionTests
{
    [Fact]
    public async Task Dictionary_projection_round_trips_its_entries()
    {
        var received = new List<Dictionary<string, int>>();
        using var h = new RunLocalHarness<TallyEvent>();

        h.Hooks.On<TallyEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Counts)
            .RunLocal((counts, ctx) => received.Add(counts));

        var counts = new Dictionary<string, int> { ["alpha"] = 1, ["beta"] = 2, ["gamma"] = 3 };
        await h.PublishAsync(new TallyEvent(3, counts));
        await h.PublishAsync(new TallyEvent(99, counts));

        Assert.Equal(counts, Assert.Single(received));
    }
}
