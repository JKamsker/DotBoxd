namespace SafeIR;

using System.Collections.ObjectModel;

public sealed record CapabilityGrant(
    string Id,
    IReadOnlyDictionary<string, string> Parameters,
    DateTimeOffset? ExpiresAt = null,
    string GrantedBy = "host-policy",
    string Reason = "")
{
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(Parameters, StringComparer.Ordinal));
}

public sealed record SandboxPolicy(
    string PolicyId,
    SandboxEffect AllowedEffects,
    IReadOnlyList<CapabilityGrant> Grants,
    ResourceLimits ResourceLimits,
    bool Deterministic = false,
    DateTimeOffset? LogicalNow = null,
    ulong? RandomSeed = null,
    IReadOnlySet<string>? DeclaredOpaqueIdTypes = null)
{
    private readonly string _policyId = PolicyId;
    private readonly SandboxEffect _allowedEffects = AllowedEffects;
    private IReadOnlyList<CapabilityGrant> _grants = ModelCopy.List(Grants);
    private readonly ResourceLimits _resourceLimits = ResourceLimits;
    private readonly bool _deterministic = Deterministic;
    private readonly DateTimeOffset? _logicalNow = LogicalNow;
    private readonly ulong? _randomSeed = RandomSeed;
    private IReadOnlySet<string> _declaredOpaqueIdTypes = NormalizeOpaqueIdTypes(DeclaredOpaqueIdTypes);

    // Lazily computed and cached so the canonical hash is built at most once per distinct
    // policy instance. Every hash-relevant property resets this so `with` copies recompute,
    // preserving the original recompute-on-change semantics while removing redundant work.
    private Lazy<string>? _hash;

    // Immutable capability index built at most once per distinct policy instance and
    // shared across the lifetime of the run. Keyed by capability id so each lookup is
    // a single O(1) probe plus an O(candidates) active-grant check (typically one
    // candidate) instead of an O(grant-count) scan. Reset when the grant list changes.
    private Lazy<Dictionary<string, CapabilityGrant[]>>? _grantIndex;

    public string PolicyId { get => _policyId; init { _policyId = value; ResetHash(); } }

    public SandboxEffect AllowedEffects { get => _allowedEffects; init { _allowedEffects = value; ResetHash(); } }

    public IReadOnlyList<CapabilityGrant> Grants
    {
        get => _grants;
        init { _grants = ModelCopy.List(value); _grantIndex = null; ResetHash(); }
    }

    public ResourceLimits ResourceLimits { get => _resourceLimits; init { _resourceLimits = value; ResetHash(); } }

    public bool Deterministic { get => _deterministic; init { _deterministic = value; ResetHash(); } }

    public DateTimeOffset? LogicalNow { get => _logicalNow; init { _logicalNow = value; ResetHash(); } }

    public ulong? RandomSeed { get => _randomSeed; init { _randomSeed = value; ResetHash(); } }

    /// <summary>
    /// Host-declared opaque-id brand type names this policy permits a module to use, in
    /// type or literal position. Empty by default (fail-closed): a module that references
    /// an opaque-id brand the host did not declare fails validation with E-POLICY-OPAQUE-ID.
    /// </summary>
    public IReadOnlySet<string> DeclaredOpaqueIdTypes
    {
        get => _declaredOpaqueIdTypes;
        init { _declaredOpaqueIdTypes = NormalizeOpaqueIdTypes(value); ResetHash(); }
    }

    public string Hash => (_hash ??= CreateHashCache()).Value;

    private static IReadOnlySet<string> NormalizeOpaqueIdTypes(IReadOnlySet<string>? declared)
        => declared is null || declared.Count == 0
            ? EmptyOpaqueIdTypes
            : new HashSet<string>(declared, StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> EmptyOpaqueIdTypes =
        new HashSet<string>(StringComparer.Ordinal);

    public DateTimeOffset GrantClock
        => Deterministic && LogicalNow is not null ? LogicalNow.Value : DateTimeOffset.UtcNow;

    public bool GrantsCapability(string capabilityId)
    {
        return TryGetActiveGrant(capabilityId, GrantClock, out _);
    }

    // Membership check against an explicit validation-pass clock. Lets a single
    // validation pass capture GrantClock once (which is DateTimeOffset.UtcNow for
    // nondeterministic policies) and reuse it for every capability probe instead of
    // re-reading the wall clock per capability, keeping the pass internally consistent.
    public bool GrantsCapability(string capabilityId, DateTimeOffset now)
    {
        return TryGetActiveGrant(capabilityId, now, out _);
    }

    public CapabilityGrant GetGrant(string capabilityId)
    {
        return TryGetActiveGrant(capabilityId, GrantClock, out var grant)
            ? grant
            : throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.PermissionDenied,
                $"capability {capabilityId} is not granted"));
    }

    public bool TryGetGrant(string capabilityId, out CapabilityGrant grant)
        => TryGetActiveGrant(capabilityId, GrantClock, out grant);

    // Single O(1) indexed lookup by capability id, then a per-id active-grant check
    // (typically one candidate). Expiry is evaluated against the supplied clock so the
    // runtime path keeps its original call-time semantics (passing live GrantClock),
    // while a validation pass can pass one captured clock for every probe. The first
    // matching grant in original list order is returned to preserve FirstOrDefault order.
    private bool TryGetActiveGrant(string capabilityId, DateTimeOffset now, out CapabilityGrant grant)
    {
        var index = (_grantIndex ??= CreateGrantIndexCache()).Value;
        if (index.TryGetValue(capabilityId, out var candidates))
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate.ExpiresAt is null || candidate.ExpiresAt > now)
                {
                    grant = candidate;
                    return true;
                }
            }
        }

        grant = null!;
        return false;
    }

    private Lazy<Dictionary<string, CapabilityGrant[]>> CreateGrantIndexCache()
        => new(() => BuildGrantIndex(_grants), LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dictionary<string, CapabilityGrant[]> BuildGrantIndex(IReadOnlyList<CapabilityGrant> grants)
    {
        var buckets = new Dictionary<string, List<CapabilityGrant>>(StringComparer.Ordinal);
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (!buckets.TryGetValue(grant.Id, out var bucket))
            {
                bucket = [];
                buckets.Add(grant.Id, bucket);
            }

            bucket.Add(grant);
        }

        var index = new Dictionary<string, CapabilityGrant[]>(buckets.Count, StringComparer.Ordinal);
        foreach (var (id, bucket) in buckets)
        {
            index.Add(id, bucket.ToArray());
        }

        return index;
    }

    private void ResetHash() => _hash = null;

    private Lazy<string> CreateHashCache()
        => new(() => PolicyHash.Compute(this), LazyThreadSafetyMode.ExecutionAndPublication);
}
