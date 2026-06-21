namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted string-intrinsic filters that lower and run server-side: a string-equality comparison
/// against a literal and a <c>string.Length</c> comparison (the string analogue of the covered <c>list.Count</c>
/// filters). Only events satisfying the predicate cross the wire.
/// </summary>
public sealed class StringFilterTests
{
    [Fact]
    public async Task String_equality_filter_runs_server_side()
    {
        var received = new List<Guid>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Zone == "crypt")
            .Select(e => e.EncounterId)
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(SampleEvents.Matching);                          // Zone "crypt" -> matches
        await h.PublishAsync(SampleEvents.Matching with { Zone = "void" });   // Zone "void"  -> filtered

        Assert.Equal(SampleEvents.SampleId, Assert.Single(received));
    }

    [Fact]
    public async Task String_equality_filter_on_a_flat_event()
    {
        var received = new List<int>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.MonsterId == "boss")
            .Select(e => e.Distance)
            .RunLocal((distance, ctx) => received.Add(distance));

        await h.PublishAsync(new ChainAggroEvent("boss", 7));
        await h.PublishAsync(new ChainAggroEvent("minion", 7));

        Assert.Equal(7, Assert.Single(received));
    }

    [Fact]
    public async Task String_length_filter_runs_server_side()
    {
        // .Where(e => e.Zone.Length > 3): the string.Length intrinsic runs server-side and discriminates.
        var received = new List<string>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Zone.Length > 3)
            .Select(e => e.Zone)
            .RunLocal((zone, ctx) => received.Add(zone));

        await h.PublishAsync(SampleEvents.Matching);                        // "crypt" length 5 > 3 -> matches
        await h.PublishAsync(SampleEvents.Matching with { Zone = "ab" });   // "ab"    length 2 > 3 -> filtered

        Assert.Equal("crypt", Assert.Single(received));
    }
}
