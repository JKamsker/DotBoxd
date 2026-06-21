using System.Runtime.Serialization;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// A non-wire behaviour object (no public properties, so not marshaller-eligible) standing in for a
/// lazily-resolved context like a player snapshot. Carried only as an <see cref="IgnoreDataMemberAttribute"/>
/// member, it must be skipped by the readers rather than blocking the chain.
/// </summary>
public sealed class AmbientScope
{
    private int _reads;

    public bool Allows(int code)
    {
        _reads++;
        return code > 0;
    }
}

/// <summary>
/// A base event carrying a non-wire, lazily-resolved member excluded from serialization via
/// <see cref="IgnoreDataMemberAttribute"/> — the same shape a real platform uses for a lazy per-actor context
/// snapshot. Its public getter would be read by the event-property readers, so without honouring
/// <c>[IgnoreDataMember]</c> the non-marshallable <see cref="Scope"/> type makes the whole chain fail to lower.
/// </summary>
public abstract record ScopedGameEvent
{
    [IgnoreDataMember]
    public AmbientScope Scope { get; init; } = new();
}

/// <summary>A derived sealed positional record carrying rich wire fields and inheriting the ignored member.</summary>
public sealed record ScopedEncounterEvent(
    Guid EncounterId,
    GamePhase Phase,
    int Distance,
    string Zone,
    int[] MonsterIds) : ScopedGameEvent;

/// <summary>
/// A whole-event <c>RunLocal</c> on an event that inherits a non-wire <c>[IgnoreDataMember]</c> member of a
/// non-marshallable type must lower and round-trip its wire (positional) fields. The ignored member is excluded
/// by all three readers — the analyzer kernel parameters, the runtime convention adapter, and the decode-side
/// record shape — so it never crosses the wire and the remaining fields stay in lockstep across encode/decode.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string ScopedWholeEventSource = Prelude + """
        public static class ScopedWholeEventUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.ScopedEncounterEvent>().Where(e => e.Distance <= 4).RunLocal((e, ctx) => { });
        }
        """;

    [Fact]
    public async Task Whole_event_inheriting_an_ignored_member_lowers_and_round_trips_its_wire_fields()
    {
        // Distance 3 <= 4 matches; the inherited [IgnoreDataMember] Scope (a non-marshallable behaviour object)
        // must not stop this from lowering. Before honouring [IgnoreDataMember] the chain failed to lower and the
        // RunLocal stub threw at install.
        var matching = new ScopedEncounterEvent(
            SampleId, GamePhase.Victory, Distance: 3, Zone: "crypt", MonsterIds: [3, 1, 4, 1, 5]);
        var filtered = matching with { Distance = 99 };

        var payload = await PushFirstMatching(ScopedWholeEventSource, matching, filtered);

        // Round-trips identically over both decode paths (reflective fallback + generated reflection-free reader).
        AssertScopedEncounter(DecodeReflective<ScopedEncounterEvent>(payload));
        AssertScopedEncounter(DecodeGenerated<ScopedEncounterEvent>(ScopedWholeEventSource, payload));
    }

    private static void AssertScopedEncounter(ScopedEncounterEvent received)
    {
        Assert.Equal(SampleId, received.EncounterId);                 // Guid wire field survives
        Assert.Equal(GamePhase.Victory, received.Phase);             // enum value survives
        Assert.Equal(3, received.Distance);
        Assert.Equal("crypt", received.Zone);
        Assert.Equal(new[] { 3, 1, 4, 1, 5 }, received.MonsterIds);  // array elements + order survive

        // The ignored, non-wire member was never marshalled: had it been treated as a wire field, encode would
        // have produced an extra slot the 5-parameter constructor could not align, corrupting the fields above.
        // On decode it is simply its default initializer, not a carried value.
        Assert.NotNull(received.Scope);
    }
}
