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

    private const string AnonymousTerminalSource = Prelude + """
        public static class AnonymousTerminalUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new { Id = e.EncounterId, Zone = e.Zone })
                    .RunLocal((x, ctx) => { });
        }
        """;

    [Fact]
    public void Anonymous_terminal_projection_is_skipped_and_emits_valid_code()
    {
        // An anonymous type as the PUSHED value cannot be intercepted (the interceptor handler parameter and the
        // decoder must name the projected type). The chain is skipped rather than emitting a ReadProjected that
        // names <anonymous type> — so generation stays valid. Compile asserts emit success internally.
        _ = Compile(AnonymousTerminalSource, enableInterceptors: true);
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

    // P5 fail-safe: a projected DTO whose field is derived in the constructor body (not a constructor parameter)
    // cannot be expressed as record.new — every persisted field must be a passed argument. Rather than silently
    // drop the derived field, the chain fails safe: it is skipped and no projection IR is emitted.
    private const string DerivedFieldSource = Prelude + """
        public sealed class DerivedInfo
        {
            public string Zone { get; }
            public int ZoneLength { get; }     // derived in the constructor, NOT a constructor parameter
            public DerivedInfo(string zone)
            {
                Zone = zone;
                ZoneLength = zone.Length;
            }
        }

        public static class DerivedFieldUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new DerivedInfo(e.Zone))
                    .RunLocal((info, ctx) => { });
        }
        """;

    [Fact]
    public void Dto_with_a_constructor_derived_field_fails_safe_instead_of_dropping_it()
    {
        // DerivedInfo.ZoneLength is set only in the ctor body, so it is not one of record.new's arguments. The chain
        // is skipped (not lowered) rather than emitting a 1-field record that silently omits ZoneLength — and the
        // generated code stays valid. Compile asserts emit success internally.
        _ = Compile(DerivedFieldSource, enableInterceptors: true);
        Assert.DoesNotContain("record.new", GeneratedSource(DerivedFieldSource));
    }

    private const string NonScalarEqualitySource = Prelude + """
        public static class NonScalarEqualityUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.ScoreEvent>().Where(e => e.Scores == e.Scores)
                    .Select(e => e.Threshold)
                    .RunLocal((threshold, ctx) => { });
        }
        """;

    private const string InheritedDtoSource = Prelude + """
        public record BaseInfo(string Zone);
        public sealed record DerivedShape(string Zone, int Distance) : BaseInfo(Zone);

        public static class InheritedDtoUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new DerivedShape(e.Zone, e.Distance))
                    .RunLocal((shape, ctx) => { });
        }
        """;

    private const string ConvertingCtorSource = Prelude + """
        public sealed class ConvertingInfo
        {
            public int Distance { get; }
            public ConvertingInfo(long distance) => Distance = (int)distance;  // param type (long) != field type (int)
        }

        public static class ConvertingCtorUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new ConvertingInfo(e.Distance))
                    .RunLocal((info, ctx) => { });
        }
        """;

    [Fact]
    public void Equality_on_non_scalar_operands_is_rejected_and_the_chain_is_skipped()
    {
        // e.Scores == e.Scores compares two List<int> values. C# `==` is reference equality there, but the sandbox
        // compares structurally — so the predicate's meaning would change. The chain fails safe (skipped), not
        // lowered to a structural list comparison.
        _ = Compile(NonScalarEqualitySource, enableInterceptors: true);
        Assert.DoesNotContain("ReadProjected", GeneratedSource(NonScalarEqualitySource));
    }

    [Fact]
    public void Projection_of_a_dto_that_inherits_public_properties_fails_safe()
    {
        // DerivedShape inherits Zone from BaseInfo; RecordFields (and the runtime marshaller) see only declared
        // members, so the base property would be silently dropped. The chain is skipped instead.
        _ = Compile(InheritedDtoSource, enableInterceptors: true);
        Assert.DoesNotContain("ReadProjected", GeneratedSource(InheritedDtoSource));
    }

    [Fact]
    public void Projection_with_a_converting_constructor_fails_safe()
    {
        // ConvertingInfo's ctor takes a long but the field is int; record.new declares the field's (int) sandbox
        // type while the value flows from the long parameter. The exact param/field type-match guard rejects it.
        _ = Compile(ConvertingCtorSource, enableInterceptors: true);
        Assert.DoesNotContain("ReadProjected", GeneratedSource(ConvertingCtorSource));
    }
}
