namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// P4 coverage: anonymous-object projections as INTERMEDIATE server-side stages. An anonymous type lowers to the
/// same <c>record.new</c> as a named DTO, and a downstream <c>Where</c>/<c>Select</c> reads its fields via
/// <c>record.get</c> — all server-side. The anonymous value is never pushed (the terminal projects a NAMED type),
/// because the generated interceptor cannot name an anonymous type for the pushed value. Shares the
/// <see cref="RemoteRunLocalChainRuntimeTests"/> harness.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string AnonymousIntermediateSource = Prelude + """
        public static class AnonymousIntermediateUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new { Id = e.EncounterId, Dist = e.Distance })
                    .Where(x => x.Dist <= 3)
                    .Select(x => x.Id)
                    .RunLocal((id, ctx) => { });
        }
        """;

    private const string AnonymousMultiFieldSource = Prelude + """
        public static class AnonymousMultiFieldUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new { Zone = e.Zone, Score = e.Score, Boss = e.Boss })
                    .Where(x => x.Score > 1_000_000_000L && x.Boss)
                    .Select(x => x.Zone)
                    .RunLocal((zone, ctx) => { });
        }
        """;

    [Fact]
    public async Task Anonymous_intermediate_projection_filters_then_projects_a_named_terminal()
    {
        // Select(e => new { Id, Dist }) builds an anonymous record server-side; .Where(x => x.Dist <= 3) reads its
        // field via record.get; the terminal Select(x => x.Id) projects a NAMED Guid that is the pushed value.
        var payload = await PushFirstMatching(
            AnonymousIntermediateSource,
            Matching,                          // Dist 3 <= 3 -> matches, terminal Id = SampleId
            Matching with { Distance = 4 });   // leading Where passes (4 <= 4) but Dist 4 <= 3 -> filtered

        Assert.Equal(SampleId, DecodeReflective<Guid>(payload));
        Assert.Equal(SampleId, DecodeGenerated<Guid>(AnonymousIntermediateSource, payload));
    }

    [Fact]
    public async Task Anonymous_intermediate_projection_with_multiple_fields_filters_server_side()
    {
        // A wider anonymous tuple (string/long/bool) filtered on two of its fields, then a named terminal projection.
        var payload = await PushFirstMatching(
            AnonymousMultiFieldSource,
            Matching,                          // Score 9e9 > 1e9 && Boss -> matches, terminal Zone = "crypt"
            Matching with { Boss = false });   // Boss false -> filtered downstream

        Assert.Equal("crypt", DecodeReflective<string>(payload));
        Assert.Equal("crypt", DecodeGenerated<string>(AnonymousMultiFieldSource, payload));
    }
}
