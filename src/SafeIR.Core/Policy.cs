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
    ulong? RandomSeed = null)
{
    public IReadOnlyList<CapabilityGrant> Grants { get; init; } = Grants.ToArray();

    public string Hash => StableHash();

    public bool GrantsCapability(string capabilityId)
    {
        var now = DateTimeOffset.UtcNow;
        return Grants.Any(g => IsActiveGrant(g, capabilityId, now));
    }

    public CapabilityGrant GetGrant(string capabilityId)
    {
        var now = DateTimeOffset.UtcNow;
        return Grants.FirstOrDefault(g => IsActiveGrant(g, capabilityId, now)) ??
           throw new SandboxRuntimeException(new SandboxError(
               SandboxErrorCode.PermissionDenied,
               $"capability {capabilityId} is not granted"));
    }

    private static bool IsActiveGrant(CapabilityGrant grant, string capabilityId, DateTimeOffset now)
        => StringComparer.Ordinal.Equals(grant.Id, capabilityId) &&
           (grant.ExpiresAt is null || grant.ExpiresAt > now);

    private string StableHash() => PolicyHash.Compute(this);
}

public sealed class SandboxPolicyBuilder
{
    private readonly List<CapabilityGrant> _grants = [];
    private SandboxEffect _allowedEffects = SandboxEffects.Pure;
    private ResourceLimits _limits = new();
    private bool _deterministic;
    private DateTimeOffset? _logicalNow;
    private ulong? _randomSeed;
    private string _policyId = "default";

    public static SandboxPolicyBuilder Create() => new();

    public SandboxPolicyBuilder WithPolicyId(string policyId)
    {
        _policyId = policyId;
        return this;
    }

    public SandboxPolicyBuilder AllowPureComputation()
    {
        _allowedEffects |= SandboxEffects.Pure;
        return this;
    }

    public SandboxPolicyBuilder Grant(string capabilityId, object parameters)
        => Grant(capabilityId, parameters, SandboxEffect.None);

    public SandboxPolicyBuilder Grant(
        string capabilityId,
        object parameters,
        SandboxEffect allowedEffects,
        Func<ResourceLimits, ResourceLimits>? configureLimits = null)
    {
        _allowedEffects |= allowedEffects;
        _grants.Add(new CapabilityGrant(capabilityId, ParameterReader.Read(parameters)));
        if (configureLimits is not null)
        {
            _limits = configureLimits(_limits);
        }

        return this;
    }

    public SandboxPolicyBuilder GrantFileRead(string root, long maxBytesPerRun)
    {
        ThrowIfNegative(maxBytesPerRun, nameof(maxBytesPerRun));
        _allowedEffects |= SandboxEffect.FileRead;
        _grants.Add(new CapabilityGrant("file.read", new Dictionary<string, string>
        {
            ["root"] = root,
            ["maxBytesPerRun"] = maxBytesPerRun.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        _limits = _limits with { MaxFileBytesRead = maxBytesPerRun };
        return this;
    }

    public SandboxPolicyBuilder GrantFileWrite(
        string root,
        long maxBytesPerRun,
        bool allowCreate = true,
        bool allowOverwrite = true)
    {
        ThrowIfNegative(maxBytesPerRun, nameof(maxBytesPerRun));
        _allowedEffects |= SandboxEffect.FileWrite | SandboxEffect.Audit;
        _grants.Add(new CapabilityGrant("file.write", new Dictionary<string, string>
        {
            ["root"] = root,
            ["maxBytesPerRun"] = maxBytesPerRun.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allowCreate"] = allowCreate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allowOverwrite"] = allowOverwrite.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        _limits = _limits with { MaxFileBytesWritten = Math.Max(_limits.MaxFileBytesWritten, maxBytesPerRun) };
        return this;
    }

    public SandboxPolicyBuilder GrantTimeNow()
    {
        _allowedEffects |= SandboxEffect.Time;
        _grants.Add(new CapabilityGrant("time.now", new Dictionary<string, string>()));
        return this;
    }

    public SandboxPolicyBuilder GrantRandom()
    {
        _allowedEffects |= SandboxEffect.Random;
        _grants.Add(new CapabilityGrant("random", new Dictionary<string, string>()));
        return this;
    }

    public SandboxPolicyBuilder GrantLogging()
    {
        _allowedEffects |= SandboxEffect.Audit;
        _grants.Add(new CapabilityGrant("log.write", new Dictionary<string, string>()));
        return this;
    }
    public SandboxPolicyBuilder GrantGameMessageWrite()
    {
        _allowedEffects |= SandboxEffect.GameStateWrite | SandboxEffect.Audit;
        _grants.Add(new CapabilityGrant("game.message.write", new Dictionary<string, string>()));
        return this;
    }
    public SandboxPolicyBuilder WithFuel(long maxFuel)
    {
        _limits = _limits with { MaxFuel = maxFuel };
        return this;
    }
    public SandboxPolicyBuilder WithMaxLoopIterations(long iterations)
    {
        _limits = _limits with { MaxLoopIterations = iterations };
        return this;
    }
    public SandboxPolicyBuilder WithMaxHostCalls(int calls)
    {
        _limits = _limits with { MaxHostCalls = calls };
        return this;
    }
    public SandboxPolicyBuilder WithMaxCallDepth(int depth)
    {
        _limits = _limits with { MaxCallDepth = depth };
        return this;
    }

    public SandboxPolicyBuilder WithWallTime(TimeSpan maxWallTime)
    {
        _limits = _limits with { MaxWallTime = maxWallTime };
        return this;
    }

    public SandboxPolicyBuilder WithMaxAllocatedBytes(long bytes)
    {
        _limits = _limits with { MaxAllocatedBytes = bytes };
        return this;
    }

    public SandboxPolicyBuilder WithMaxListLength(int length)
    {
        _limits = _limits with { MaxListLength = length };
        return this;
    }

    public SandboxPolicyBuilder WithMaxMapEntries(int entries)
    {
        _limits = _limits with { MaxMapEntries = entries };
        return this;
    }

    public SandboxPolicyBuilder WithMaxCollectionDepth(int depth)
    {
        _limits = _limits with { MaxCollectionDepth = depth };
        return this;
    }

    public SandboxPolicyBuilder WithMaxTotalCollectionElements(long elements)
    {
        _limits = _limits with { MaxTotalCollectionElements = elements };
        return this;
    }

    public SandboxPolicyBuilder WithMaxLogEvents(int events)
    {
        _limits = _limits with { MaxLogEvents = events };
        return this;
    }

    public SandboxPolicyBuilder WithMaxLogMessageLength(int length)
    {
        _limits = _limits with { MaxLogMessageLength = length };
        return this;
    }

    public SandboxPolicyBuilder WithMaxStringLength(int length)
    {
        _limits = _limits with { MaxStringLength = length };
        return this;
    }

    public SandboxPolicyBuilder WithMaxTotalStringBytes(long bytes)
    {
        _limits = _limits with { MaxTotalStringBytes = bytes };
        return this;
    }

    public SandboxPolicyBuilder Deterministic(DateTimeOffset logicalNow, ulong randomSeed)
    {
        _deterministic = true;
        _logicalNow = logicalNow;
        _randomSeed = randomSeed;
        return this;
    }

    public SandboxPolicy Build()
    {
        ResourceLimitValidation.Validate(_limits);
        return new SandboxPolicy(_policyId, _allowedEffects, _grants.ToArray(), _limits, _deterministic, _logicalNow, _randomSeed);
    }

    private static void ThrowIfNegative(long value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }
}

internal static class ParameterReader
{
    public static IReadOnlyDictionary<string, string> Read(object parameters)
    {
        if (parameters is IReadOnlyDictionary<string, string> values)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(values, StringComparer.Ordinal));
        }

        var dictionary = parameters.GetType().GetProperties()
            .ToDictionary(
                p => p.Name,
                p => Convert.ToString(p.GetValue(parameters), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, string>(dictionary);
    }
}
