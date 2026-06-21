namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>A projected DTO whose <c>ZoneLength</c> field is derived in the constructor body rather than passed as a
/// parameter — it cannot be expressed as a <c>record.new</c>, so the chain fails safe.</summary>
public sealed class DerivedInfo
{
    public string Zone { get; }

    public int ZoneLength { get; }

    public DerivedInfo(string zone)
    {
        Zone = zone;
        ZoneLength = zone.Length;
    }
}

/// <summary>A base record carrying a public property a derived projection would inherit.</summary>
public record BaseInfo(string Zone);

/// <summary>A projected DTO that inherits a public property (<c>Zone</c>) from <see cref="BaseInfo"/>; the marshaller
/// only sees declared members, so the inherited one would be silently dropped — the chain fails safe.</summary>
public sealed record DerivedShape(string Zone, int Distance) : BaseInfo(Zone);

/// <summary>A projected DTO whose constructor parameter type (<c>long</c>) differs from its field type (<c>int</c>),
/// which the exact-type-match guard rejects — the chain fails safe.</summary>
public sealed class ConvertingInfo
{
    public int Distance { get; }

    public ConvertingInfo(long distance) => Distance = (int)distance;
}

/// <summary>An event with two list fields, so a non-scalar equality comparison can be written between distinct
/// operands (avoiding a self-comparison warning).</summary>
public sealed record TwoListEvent(int Distance, List<int> Left, List<int> Right);

/// <summary>
/// Fail-safe RUNTIME behaviour: chains the generator deliberately refuses to lower (a constructor-derived DTO field,
/// an inherited-property DTO, a converting constructor, and a non-scalar equality predicate) are not intercepted, so
/// the real remote terminal throws <see cref="NotSupportedException"/> at runtime rather than silently corrupting or
/// dropping data. (The generated-source absence of these is asserted in DotBoxD.Kernels.Tests; this covers the
/// runtime side.)
/// </summary>
public sealed class FailSafeTests
{
    [Fact]
    public void Constructor_derived_field_projection_is_not_intercepted()
    {
        using var h = new RunLocalHarness<EncounterEvent>();

        Assert.Throws<NotSupportedException>(() =>
        {
            h.Hooks.On<EncounterEvent>()
                .Where(e => e.Distance <= 4)
                .Select(e => new DerivedInfo(e.Zone))
                .RunLocal((info, ctx) => { });
        });
    }

    [Fact]
    public void Inherited_property_dto_projection_is_not_intercepted()
    {
        using var h = new RunLocalHarness<EncounterEvent>();

        Assert.Throws<NotSupportedException>(() =>
        {
            h.Hooks.On<EncounterEvent>()
                .Where(e => e.Distance <= 4)
                .Select(e => new DerivedShape(e.Zone, e.Distance))
                .RunLocal((shape, ctx) => { });
        });
    }

    [Fact]
    public void Converting_constructor_projection_is_not_intercepted()
    {
        using var h = new RunLocalHarness<EncounterEvent>();

        Assert.Throws<NotSupportedException>(() =>
        {
            h.Hooks.On<EncounterEvent>()
                .Where(e => e.Distance <= 4)
                .Select(e => new ConvertingInfo(e.Distance))
                .RunLocal((info, ctx) => { });
        });
    }

    [Fact]
    public void Non_scalar_equality_predicate_is_not_intercepted()
    {
        using var h = new RunLocalHarness<TwoListEvent>();

        Assert.Throws<NotSupportedException>(() =>
        {
            h.Hooks.On<TwoListEvent>()
                .Where(e => e.Left == e.Right)
                .Select(e => e.Distance)
                .RunLocal((distance, ctx) => { });
        });
    }
}
