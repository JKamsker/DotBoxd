using System.Numerics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>A record struct nesting a public-field struct (System.Numerics.Vector3), mirroring a map location.</summary>
public readonly record struct MapPoint(int MapId, Vector3 Position);

/// <summary>
/// An event carrying a <c>float</c> scalar, a public-field value type (<see cref="Vector3"/>, whose X/Y/Z are
/// float <i>fields</i>, not properties), and a record struct nesting one — plus a scalar the Where filters on.
/// Proves a whole-event RunLocal push marshals float and field-only structs end to end.
/// </summary>
public sealed record FieldStructEvent(
    Guid Id,
    int Distance,
    float Health,
    Vector3 Velocity,
    MapPoint Spot,
    string Zone);

/// <summary>
/// Whole-event <c>RunLocal</c> coverage for <c>float</c> (widened to the sandbox F64 kind) and public-field
/// value types (a field-only struct marshals as a record of its fields, reconstructed through its constructor).
/// Asserts field-level fidelity over both decode paths.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string FieldStructWholeEventSource = Prelude + """
        public static class FieldStructWholeEventUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.FieldStructEvent>().Where(e => e.Distance <= 4).RunLocal((e, ctx) => { });
        }
        """;

    [Fact]
    public async Task Whole_event_with_float_and_public_field_struct_round_trips_with_field_fidelity()
    {
        // All float literals are exactly representable, so the float -> F64 -> float widen/narrow is lossless.
        var matching = new FieldStructEvent(
            SampleId,
            Distance: 3,
            Health: 0.5f,
            Velocity: new Vector3(1.5f, -2.25f, 3f),
            Spot: new MapPoint(42, new Vector3(10f, 20f, 30f)),
            Zone: "crypt");
        var filtered = matching with { Distance = 99 };

        var payload = await PushFirstMatching(FieldStructWholeEventSource, matching, filtered);

        AssertFieldStruct(DecodeReflective<FieldStructEvent>(payload));
        AssertFieldStruct(DecodeGenerated<FieldStructEvent>(FieldStructWholeEventSource, payload));
    }

    private static void AssertFieldStruct(FieldStructEvent received)
    {
        Assert.Equal(SampleId, received.Id);
        Assert.Equal(3, received.Distance);
        Assert.Equal(0.5f, received.Health);                                     // float scalar survives
        Assert.Equal(new Vector3(1.5f, -2.25f, 3f), received.Velocity);          // field-only struct survives
        Assert.Equal(new MapPoint(42, new Vector3(10f, 20f, 30f)), received.Spot); // nested field-struct survives
        Assert.Equal("crypt", received.Zone);
    }
}
