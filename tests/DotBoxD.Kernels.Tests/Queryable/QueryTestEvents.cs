namespace DotBoxD.Kernels.Tests.Queryable;

/// <summary>Shared event/DTO shapes for the DotBoxD.Queryable tests, independent of the GameServer sample.</summary>
public sealed record AttackTestEvent(string AttackerId, string TargetId, int Damage, int AttackerLevel);

/// <summary>A projected payload mirroring the issue's <c>AttackNotice</c> DTO.</summary>
public sealed record AttackNotice(string AttackerId, string TargetId, int Damage);

/// <summary>A mutable projected payload with constructor arguments plus object-initializer assignments.</summary>
public sealed class MutableAttackNotice(string attackerId)
{
    public string AttackerId { get; } = attackerId;

    public string TargetId { get; set; } = string.Empty;

    public int Damage { get; set; }
}

/// <summary>An event with a nested member, used to exercise dotted member paths.</summary>
public sealed record SourcedTestEvent(NestedSource Source, int Value, bool Flagged);

/// <summary>A nested object reachable via <c>e.Source.Id</c>.</summary>
public sealed record NestedSource(string Id, string Region);

/// <summary>An event with a nullable member, used to exercise <c>e.Key == null</c> routing.</summary>
public sealed record NullableTestEvent(string? Key, int Value);

/// <summary>An event with a nullable value member, used to exercise nullable query translation.</summary>
public sealed record NullableValueTestEvent(int? Score);

/// <summary>An event with a floating-point member, used to exercise numeric-kind routing-key matching.</summary>
public sealed record MetricTestEvent(string Id, double Score);

/// <summary>An event whose <see cref="Boom"/> getter throws, used to verify dispatch isolation.</summary>
public sealed record ThrowingGetterEvent(string Id)
{
    public string Boom => throw new InvalidTimeZoneException("boom");
}

/// <summary>An event with a <see cref="ulong"/> member exceeding <see cref="long.MaxValue"/>.</summary>
public sealed record UnsignedTestEvent(ulong Big);
