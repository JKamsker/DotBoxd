namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>A DTO carrying a list field, used to read <c>.Count</c> off a projected record field (record.get
/// then list.count — a two-hop member chain).</summary>
public sealed record Party(int Size, System.Collections.Generic.List<int> MemberIds);

/// <summary>
/// Member-chain coverage (P3): a downstream <c>Where</c>/<c>Select</c> reading <c>.Count</c>/<c>.Length</c> off a
/// list value — whether that list is the projected element, a list field of a projected DTO, or an event
/// property read in a leading <c>Where</c>. The <c>list.count</c> intrinsic runs server-side, so the count
/// discriminates before any push. Shares the harness with the main <see cref="RemoteRunLocalChainRuntimeTests"/>.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string CountOnProjectedListSource = Prelude + """
        public static class CountOnProjectedListUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.ScoreEvent>().Where(e => e.Threshold <= 4)
                    .Select(e => e.Scores)
                    .Where(s => s.Count > 2)
                    .RunLocal((s, ctx) => { });
        }
        """;

    private const string CountOnDtoListFieldSource = Prelude + """
        public static class CountOnDtoListFieldUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.ScoreEvent>().Where(e => e.Threshold <= 99)
                    .Select(e => new Ev.Party(e.Threshold, e.Scores))
                    .Where(p => p.MemberIds.Count > 2)
                    .RunLocal((p, ctx) => { });
        }
        """;

    private const string CountOnEventListSource = Prelude + """
        public static class CountOnEventListUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.ScoreEvent>().Where(e => e.Scores.Count > 2)
                    .Select(e => e.Threshold)
                    .RunLocal((threshold, ctx) => { });
        }
        """;

    [Fact]
    public async Task Count_on_a_projected_list_filters_server_side()
    {
        // .Select(e => e.Scores).Where(s => s.Count > 2): the projected element is a list; the downstream Where
        // reads its Count via list.count, server-side. Both events clear the leading Where; the count discriminates.
        var payload = await PushFirstMatching(
            CountOnProjectedListSource,
            new ScoreEvent(3, [10, 20, 30]),   // Count 3 > 2 -> matches
            new ScoreEvent(3, [7, 8]));        // Count 2 > 2 -> filtered downstream

        Assert.Equal(new List<int> { 10, 20, 30 }, DecodeReflective<List<int>>(payload));
        Assert.Equal(new List<int> { 10, 20, 30 }, DecodeGenerated<List<int>>(CountOnProjectedListSource, payload));
    }

    [Fact]
    public async Task Count_on_a_list_field_of_a_projected_dto_filters_server_side()
    {
        // new Party(e.Threshold, e.Scores) then .Where(p => p.MemberIds.Count > 2): a TWO-hop chain — record.get
        // reads the list field off the projected record, then list.count reads its size, all server-side.
        var payload = await PushFirstMatching(
            CountOnDtoListFieldSource,
            new ScoreEvent(5, [10, 20, 30]),   // MemberIds.Count 3 > 2 -> matches
            new ScoreEvent(5, [9]));           // MemberIds.Count 1 > 2 -> filtered downstream

        // Party.MemberIds is a List<int>, whose reference equality breaks the record's generated Equals; compare
        // the fields structurally instead.
        foreach (var party in new[]
                 {
                     DecodeReflective<Party>(payload),
                     DecodeGenerated<Party>(CountOnDtoListFieldSource, payload),
                 })
        {
            Assert.Equal(5, party.Size);
            Assert.Equal(new[] { 10, 20, 30 }, party.MemberIds);
        }
    }

    [Fact]
    public async Task Count_on_an_event_list_property_filters_in_a_leading_where()
    {
        // .Where(e => e.Scores.Count > 2): list.count on an EVENT list property, before any Select. The matching
        // event has 3 scores; an otherwise-similar event with fewer is filtered before any push.
        var payload = await PushFirstMatching(
            CountOnEventListSource,
            new ScoreEvent(42, [1, 2, 3]),     // Scores.Count 3 > 2 -> matches, projects Threshold 42
            new ScoreEvent(7, [1]));           // Scores.Count 1 > 2 -> filtered

        Assert.Equal(42, DecodeReflective<int>(payload));
        Assert.Equal(42, DecodeGenerated<int>(CountOnEventListSource, payload));
    }
}
