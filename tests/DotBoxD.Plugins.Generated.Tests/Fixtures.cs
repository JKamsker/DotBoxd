using System.Numerics;

namespace DotBoxD.Plugins.Generated.Tests;

// Event, projection, and enum fixtures shared across the build-time-interception test suite. These mirror the
// shapes proven against the generator in DotBoxD.Kernels.Tests' RemoteRunLocal matrix, recreated here so each
// chain is authored as ordinary code the generator intercepts at build time.

/// <summary>A flat scalar event, the primary shape a plugin author subscribes to.</summary>
public sealed record ChainAggroEvent(string MonsterId, int Distance);

/// <summary>An enum event field, exercised so its value (not just 0) round-trips through the I32 wire kind.</summary>
public enum GamePhase
{
    Intro = 0,
    Battle = 2,
    Victory = 7
}

/// <summary>A <c>long</c>-backed enum, exercising the I64 enum-constant path.</summary>
public enum WideEnum : long
{
    Zero = 0,
    Wide = 5_000_000_000L
}

/// <summary>A <c>ulong</c>-backed enum whose top value exceeds <c>long.MaxValue</c>; the bit-preserving path must
/// carry it losslessly where a range-checked conversion would overflow.</summary>
public enum HugeEnum : ulong
{
    Zero = 0,
    Top = 0xFFFFFFFFFFFFFFFF
}

/// <summary>A nested DTO field, exercised for record-in-record fidelity.</summary>
public sealed record PlayerInfo(string Name, int Level);

/// <summary>A DTO projected by a <c>new Dto(...)</c> Select, carrying a Guid alongside a scalar.</summary>
public sealed record EncounterTicket(Guid EncounterId, string Zone);

/// <summary>A DTO whose first field is itself a DTO, exercising a constructed nested record projection.</summary>
public sealed record Squad(PlayerInfo Leader, string Banner);

/// <summary>A DTO with an enum field, projected via <c>new Dto(e.Id, EnumConstant)</c>.</summary>
public sealed record PhaseTicket(Guid Id, GamePhase Phase);

/// <summary>A projected DTO whose field name (<c>Range</c>) does NOT collide with any event property.</summary>
public sealed record RangedTicket(int Range, string Zone);

/// <summary>An event with two same-typed scalar fields, used to make a projected-field-vs-event-property
/// collision observable.</summary>
public sealed record DualEvent(int Near, int Far);

/// <summary>A DTO with a single field named <c>Near</c> that collides with <see cref="DualEvent.Near"/>.</summary>
public sealed record NearTicket(int Near);

/// <summary>A DTO carrying a list field, used to read <c>.Count</c> off a projected record field.</summary>
public sealed record Party(int Size, List<int> MemberIds);

/// <summary>An event carrying a <see cref="HugeEnum"/> property, exercising the marshaller's enum encode path for
/// a ulong value above <c>long.MaxValue</c> in a whole-event push.</summary>
public sealed record HugeEnumEvent(int Distance, HugeEnum Big);

/// <summary>An event with a <see cref="List{T}"/> property — a different encode/decode path than <c>int[]</c>.</summary>
public sealed record ScoreEvent(int Threshold, List<int> Scores);

/// <summary>An event carrying a <c>float</c> scalar, for a direct float projection (float widens to the F64 wire
/// kind and narrows back exactly).</summary>
public sealed record FloatEvent(int Distance, float Health);

/// <summary>An event carrying a <see cref="Dictionary{TKey, TValue}"/> with scalar (string) keys, for a map
/// projection.</summary>
public sealed record TallyEvent(int Distance, Dictionary<string, int> Counts);

/// <summary>
/// A non-positional event class whose constructor parameter order (id, zone, distance) differs from its property
/// declaration order (Distance, Id, Zone). Exercises that the whole-event wire field order is declaration order on
/// both encode and decode sides.
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
/// A rich event carrying every marshaller-eligible kind — Guid, enum, the four scalars + string, an array, and a
/// nested DTO — plus a scalar (<see cref="Distance"/>) the Where filters on.
/// </summary>
public sealed record EncounterEvent(
    Guid EncounterId,
    GamePhase Phase,
    bool Boss,
    int Distance,
    long Score,
    double Multiplier,
    string Zone,
    int[] MonsterIds,
    PlayerInfo Player);

/// <summary>A record struct nesting a public-field struct (<see cref="Vector3"/>), mirroring a map location.</summary>
public readonly record struct MapPoint(int MapId, Vector3 Position);

/// <summary>
/// An event carrying a <c>float</c> scalar, a public-field value type (<see cref="Vector3"/>, whose X/Y/Z are float
/// fields, not properties), and a record struct nesting one — plus a scalar the Where filters on.
/// </summary>
public sealed record FieldStructEvent(
    Guid Id,
    int Distance,
    float Health,
    Vector3 Velocity,
    MapPoint Spot,
    string Zone);

/// <summary>Canonical sample data for the rich <see cref="EncounterEvent"/>: a matching event and a filtered one
/// (identical but for a Distance that fails the standard <c>Distance &lt;= 4</c> filter).</summary>
internal static class SampleEvents
{
    public static readonly Guid SampleId = new("0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9");

    public static EncounterEvent Matching => new(
        SampleId, GamePhase.Victory, Boss: true, Distance: 3, Score: 9_000_000_000L, Multiplier: 1.25,
        Zone: "crypt", MonsterIds: [3, 1, 4, 1, 5], Player: new PlayerInfo("hero", 7));

    public static EncounterEvent Filtered => Matching with { Distance = 99 };
}
