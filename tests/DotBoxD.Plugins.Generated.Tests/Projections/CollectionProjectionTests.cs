namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted collection projections: a terminal <c>Select</c> projecting an <c>int[]</c> and a
/// <c>List&lt;int&gt;</c> (distinct encode/decode paths) server-side, with the native <c>RunLocal</c> delegate
/// receiving the collection with its elements and order intact.
/// </summary>
public sealed class CollectionProjectionTests
{
    [Fact]
    public async Task Int_array_projection_preserves_elements_and_order()
    {
        var received = new List<int[]>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterIds)
            .RunLocal((ids, ctx) => received.Add(ids));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(new[] { 3, 1, 4, 1, 5 }, Assert.Single(received));
    }

    [Fact]
    public async Task List_projection_preserves_elements_and_order()
    {
        // List<T> goes through a different generated/reflective decode path than int[]; cover it end-to-end.
        var received = new List<List<int>>();
        using var h = new RunLocalHarness<ScoreEvent>();

        h.Hooks.On<ScoreEvent>()
            .Where(e => e.Threshold <= 4)
            .Select(e => e.Scores)
            .RunLocal((scores, ctx) => received.Add(scores));

        await h.PublishAsync(new ScoreEvent(3, [10, 20, 30]));
        await h.PublishAsync(new ScoreEvent(99, [10, 20, 30]));

        Assert.Equal(new List<int> { 10, 20, 30 }, Assert.Single(received));
    }
}
