namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>A DTO with an enum field, projected via <c>new Dto(e.Id, EnumConstant)</c>.</summary>
public sealed record PhaseTicket(Guid Id, GamePhase Phase);

/// <summary>A <c>long</c>-backed enum, exercising the I64 enum-constant path.</summary>
public enum WideEnum : long
{
    Zero = 0,
    Wide = 5_000_000_000L
}

/// <summary>A <c>ulong</c>-backed enum whose top value exceeds <c>long.MaxValue</c> — the value a range-checked
/// <c>Convert.ToInt64</c> would overflow on; the bit-preserving path must carry it losslessly.</summary>
public enum HugeEnum : ulong
{
    Zero = 0,
    Top = 0xFFFFFFFFFFFFFFFF
}

/// <summary>An event carrying a <see cref="HugeEnum"/> property, exercising the marshaller's enum encode path
/// (ConventionEventAdapter) for a ulong value above <c>long.MaxValue</c> in a whole-event push.</summary>
public sealed record HugeEnumEvent(int Distance, HugeEnum Big);

/// <summary>An event with a <see cref="List{T}"/> property — a different encode/decode path than <c>int[]</c>.</summary>
public sealed record ScoreEvent(int Threshold, List<int> Scores);

/// <summary>
/// A NON-positional event class whose constructor parameter order (id, zone, distance) differs from its property
/// declaration order (Distance, Id, Zone). Exercises that the whole-event wire field order is declaration order on
/// BOTH the encode (adapter) and decode (marshaller) sides — before that was unified the encoder wrote constructor
/// order while the decoder read declaration order, silently misaligning the fields.
/// </summary>
public sealed class SwappedEvent
{
    public int Distance { get; }

    public Guid Id { get; }

    public string Zone { get; }

    public SwappedEvent(Guid id, string zone, int distance)
    {
        Id = id;
        Zone = zone;
        Distance = distance;
    }
}

/// <summary>
/// Additional non-scalar RunLocal shapes beyond the core rich-type matrix: a <see cref="List{T}"/> projection
/// (a distinct decode path from arrays), a non-positional event class (field-order regression from the
/// adversarial review), and enum CONSTANTS in projections and Where filters. Shares the harness/helpers with the
/// main <see cref="RemoteRunLocalChainRuntimeTests"/> partial.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string ListProjectionSource = Prelude + """
        public static class ListProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.ScoreEvent>().Where(e => e.Threshold <= 4)
                    .Select(e => e.Scores).RunLocal((scores, ctx) => { });
        }
        """;

    private const string SwappedWholeEventSource = Prelude + """
        public static class SwappedWholeEventUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.SwappedEvent>().Where(e => e.Distance <= 4).RunLocal((e, ctx) => { });
        }
        """;

    private const string EnumConstantDtoSource = Prelude + """
        public static class EnumConstantDtoUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new Ev.PhaseTicket(e.EncounterId, Ev.GamePhase.Battle)).RunLocal((ticket, ctx) => { });
        }
        """;

    private const string EnumFilterSource = Prelude + """
        public static class EnumFilterUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Phase == Ev.GamePhase.Victory)
                    .Select(e => e.EncounterId).RunLocal((id, ctx) => { });
        }
        """;

    private const string WideEnumConstantSource = Prelude + """
        public static class WideEnumConstantUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => Ev.WideEnum.Wide).RunLocal((w, ctx) => { });
        }
        """;

    [Fact]
    public async Task List_projection_preserves_elements_and_order()
    {
        // List<T> goes through a different generated/reflective decode path than int[]; cover it end-to-end.
        var payload = await PushFirstMatching(
            ListProjectionSource, new ScoreEvent(3, [10, 20, 30]), new ScoreEvent(99, [10, 20, 30]));

        Assert.Equal(new List<int> { 10, 20, 30 }, DecodeReflective<List<int>>(payload));
        Assert.Equal(new List<int> { 10, 20, 30 }, DecodeGenerated<List<int>>(ListProjectionSource, payload));
    }

    [Fact]
    public async Task Whole_event_with_constructor_order_differing_from_declaration_round_trips()
    {
        // SwappedEvent declares [Distance, Id, Zone] but its constructor is (id, zone, distance). The wire field
        // order must be declaration order on both encode and decode; the distinct field types (int/Guid/string)
        // mean any transposition throws a kind-mismatch rather than corrupting silently.
        var id = new Guid("11112222-3333-4444-5555-666677778888");
        var payload = await PushFirstMatching(
            SwappedWholeEventSource,
            new SwappedEvent(id, "crypt", 3),
            new SwappedEvent(id, "crypt", 99));

        foreach (var received in new[]
                 {
                     DecodeReflective<SwappedEvent>(payload),
                     DecodeGenerated<SwappedEvent>(SwappedWholeEventSource, payload),
                 })
        {
            Assert.Equal(id, received.Id);
            Assert.Equal("crypt", received.Zone);
            Assert.Equal(3, received.Distance);
        }
    }

    [Fact]
    public async Task New_dto_with_enum_constant_round_trips()
    {
        // Select(e => new PhaseTicket(e.EncounterId, GamePhase.Battle)): an enum CONSTANT argument lowers to its
        // underlying I32 literal, matching the DTO's enum field, and round-trips back to the enum value.
        var payload = await PushFirstMatching(EnumConstantDtoSource, Matching, Filtered);

        var expected = new PhaseTicket(SampleId, GamePhase.Battle);
        Assert.Equal(expected, DecodeReflective<PhaseTicket>(payload));
        Assert.Equal(expected, DecodeGenerated<PhaseTicket>(EnumConstantDtoSource, payload));
    }

    [Fact]
    public async Task Enum_constant_where_filter_runs_server_side()
    {
        // .Where(e => e.Phase == GamePhase.Victory): an enum-constant comparison filters server-side. Matching has
        // Phase == Victory; an otherwise-identical event with a different Phase is filtered before any push.
        var payload = await PushFirstMatching(
            EnumFilterSource, Matching, Matching with { Phase = GamePhase.Intro });

        Assert.Equal(SampleId, DecodeReflective<Guid>(payload));
    }

    [Fact]
    public async Task Wide_enum_constant_projection_round_trips()
    {
        // A long-backed enum constant lowers through the I64 path.
        var payload = await PushFirstMatching(WideEnumConstantSource, Matching, Filtered);

        Assert.Equal(WideEnum.Wide, DecodeReflective<WideEnum>(payload));
        Assert.Equal(WideEnum.Wide, DecodeGenerated<WideEnum>(WideEnumConstantSource, payload));
    }

    private const string HugeEnumConstantSource = Prelude + """
        public static class HugeEnumConstantUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => Ev.HugeEnum.Top).RunLocal((h, ctx) => { });
        }
        """;

    private const string HugeEnumEventSource = Prelude + """
        public static class HugeEnumEventUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.HugeEnumEvent>().Where(e => e.Distance <= 4).RunLocal((e, ctx) => { });
        }
        """;

    [Fact]
    public async Task Ulong_enum_constant_above_long_max_round_trips()
    {
        // HugeEnum.Top = ulong.MaxValue, above long.MaxValue: the analyzer's value-preserving (unchecked) constant
        // lowering carries the bits where a range-checked Convert.ToInt64 would overflow, and the decoder recovers it.
        var payload = await PushFirstMatching(HugeEnumConstantSource, Matching, Filtered);

        Assert.Equal(HugeEnum.Top, DecodeReflective<HugeEnum>(payload));
        Assert.Equal(HugeEnum.Top, DecodeGenerated<HugeEnum>(HugeEnumConstantSource, payload));
    }

    [Fact]
    public async Task Whole_event_with_a_ulong_enum_above_long_max_round_trips()
    {
        // Exercises the marshaller ENCODE path (ConventionEventAdapter) for a ulong enum property value above
        // long.MaxValue — the value-preserving unchecked conversion must carry it through a whole-event push.
        var payload = await PushFirstMatching(
            HugeEnumEventSource,
            new HugeEnumEvent(3, HugeEnum.Top),
            new HugeEnumEvent(99, HugeEnum.Top));

        foreach (var received in new[]
                 {
                     DecodeReflective<HugeEnumEvent>(payload),
                     DecodeGenerated<HugeEnumEvent>(HugeEnumEventSource, payload),
                 })
        {
            Assert.Equal(HugeEnum.Top, received.Big);
            Assert.Equal(3, received.Distance);
        }
    }
}
