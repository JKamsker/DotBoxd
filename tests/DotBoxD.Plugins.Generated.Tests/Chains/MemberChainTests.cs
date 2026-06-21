namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted member-chain coverage: a downstream <c>Where</c> reading <c>.Count</c> off a list value —
/// whether that list is the projected element, a list field of a projected DTO (a two-hop <c>record.get</c> then
/// <c>list.count</c>), or an event property read in a leading <c>Where</c>. The <c>list.count</c> intrinsic runs
/// server-side, so the count discriminates before any push.
/// </summary>
public sealed class MemberChainTests
{
    [Fact]
    public async Task Count_on_a_projected_list_filters_server_side()
    {
        // .Select(e => e.Scores).Where(s => s.Count > 2): the projected element is a list; the downstream Where reads
        // its Count via list.count, server-side. Both events clear the leading Where; the count discriminates.
        var received = new List<List<int>>();
        using var h = new RunLocalHarness<ScoreEvent>();

        h.Hooks.On<ScoreEvent>()
            .Where(e => e.Threshold <= 4)
            .Select(e => e.Scores)
            .Where(s => s.Count > 2)
            .RunLocal((s, ctx) => received.Add(s));

        await h.PublishAsync(new ScoreEvent(3, [10, 20, 30]));   // Count 3 > 2 -> matches
        await h.PublishAsync(new ScoreEvent(3, [7, 8]));         // Count 2 > 2 -> filtered downstream

        Assert.Equal(new List<int> { 10, 20, 30 }, Assert.Single(received));
    }

    [Fact]
    public async Task Count_on_a_list_field_of_a_projected_dto_filters_server_side()
    {
        // new Party(e.Threshold, e.Scores) then .Where(p => p.MemberIds.Count > 2): a TWO-hop chain — record.get
        // reads the list field off the projected record, then list.count reads its size, all server-side.
        var received = new List<Party>();
        using var h = new RunLocalHarness<ScoreEvent>();

        h.Hooks.On<ScoreEvent>()
            .Where(e => e.Threshold <= 99)
            .Select(e => new Party(e.Threshold, e.Scores))
            .Where(p => p.MemberIds.Count > 2)
            .RunLocal((p, ctx) => received.Add(p));

        await h.PublishAsync(new ScoreEvent(5, [10, 20, 30]));   // MemberIds.Count 3 > 2 -> matches
        await h.PublishAsync(new ScoreEvent(5, [9]));            // MemberIds.Count 1 > 2 -> filtered downstream

        // Party.MemberIds is a List<int> whose reference equality breaks the record's generated Equals; compare
        // the fields structurally instead.
        var party = Assert.Single(received);
        Assert.Equal(5, party.Size);
        Assert.Equal(new[] { 10, 20, 30 }, party.MemberIds);
    }

    [Fact]
    public async Task Count_on_an_event_list_property_filters_in_a_leading_where()
    {
        // .Where(e => e.Scores.Count > 2): list.count on an EVENT list property, before any Select. The matching
        // event has 3 scores; an otherwise-similar event with fewer is filtered before any push.
        var received = new List<int>();
        using var h = new RunLocalHarness<ScoreEvent>();

        h.Hooks.On<ScoreEvent>()
            .Where(e => e.Scores.Count > 2)
            .Select(e => e.Threshold)
            .RunLocal((threshold, ctx) => received.Add(threshold));

        await h.PublishAsync(new ScoreEvent(42, [1, 2, 3]));   // Scores.Count 3 > 2 -> matches, projects 42
        await h.PublishAsync(new ScoreEvent(7, [1]));          // Scores.Count 1 > 2 -> filtered

        Assert.Equal(42, Assert.Single(received));
    }
}
