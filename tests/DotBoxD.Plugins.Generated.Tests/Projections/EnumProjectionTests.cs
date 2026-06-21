namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted enum edge cases: a <c>long</c>-backed enum constant (I64 path), a <c>ulong</c>-backed enum
/// constant whose value exceeds <c>long.MaxValue</c> (bit-preserving path), and an enum-constant comparison in the
/// leading <c>Where</c> that discriminates server-side.
/// </summary>
public sealed class EnumProjectionTests
{
    [Fact]
    public async Task Long_backed_enum_constant_projection_round_trips()
    {
        var received = new List<WideEnum>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => WideEnum.Wide)
            .RunLocal((w, ctx) => received.Add(w));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(WideEnum.Wide, Assert.Single(received));
    }

    [Fact]
    public async Task Ulong_enum_constant_above_long_max_round_trips()
    {
        // HugeEnum.Top = ulong.MaxValue, above long.MaxValue: the value-preserving (unchecked) constant lowering
        // carries the bits where a range-checked conversion would overflow, and the decoder recovers it.
        var received = new List<HugeEnum>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => HugeEnum.Top)
            .RunLocal((value, ctx) => received.Add(value));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(HugeEnum.Top, Assert.Single(received));
    }

    [Fact]
    public async Task Enum_constant_where_filter_runs_server_side()
    {
        // .Where(e => e.Phase == GamePhase.Victory): an enum-constant comparison filters server-side. The matching
        // event has Phase == Victory; an otherwise-identical event with a different Phase is filtered before push.
        var received = new List<Guid>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Phase == GamePhase.Victory)
            .Select(e => e.EncounterId)
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(SampleEvents.Matching);                             // Phase Victory -> matches
        await h.PublishAsync(SampleEvents.Matching with { Phase = GamePhase.Intro }); // filtered

        Assert.Equal(SampleEvents.SampleId, Assert.Single(received));
    }
}
