using System.Security.Cryptography;
using System.Text;

namespace SafeIR;

public sealed record CapabilityGrant(
    string Id,
    IReadOnlyDictionary<string, string> Parameters,
    DateTimeOffset? ExpiresAt = null,
    string GrantedBy = "host-policy",
    string Reason = "");

public sealed record SandboxPolicy(
    string PolicyId,
    SandboxEffect AllowedEffects,
    IReadOnlyList<CapabilityGrant> Grants,
    ResourceLimits ResourceLimits,
    bool Deterministic = false,
    DateTimeOffset? LogicalNow = null,
    ulong? RandomSeed = null)
{
    public string Hash => StableHash();

    public bool GrantsCapability(string capabilityId)
        => Grants.Any(g => StringComparer.Ordinal.Equals(g.Id, capabilityId) &&
                           (g.ExpiresAt is null || g.ExpiresAt > DateTimeOffset.UtcNow));

    public CapabilityGrant GetGrant(string capabilityId)
        => Grants.FirstOrDefault(g => StringComparer.Ordinal.Equals(g.Id, capabilityId)) ??
           throw new SandboxRuntimeException(new SandboxError(
               SandboxErrorCode.PermissionDenied,
               $"capability {capabilityId} is not granted"));

    private string StableHash()
    {
        var builder = new StringBuilder();
        builder.Append("policy|").Append(PolicyId).Append('|').Append((int)AllowedEffects).Append('|');
        builder.Append(Deterministic).Append('|').Append(LogicalNow?.ToUnixTimeMilliseconds()).Append('|').Append(RandomSeed);
        AppendResourceLimits(builder);

        foreach (var grant in Grants.OrderBy(g => g.Id, StringComparer.Ordinal)) {
            builder.Append("|grant|").Append(grant.Id);
            foreach (var item in grant.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal)) {
                builder.Append('|').Append(item.Key).Append('=').Append(item.Value);
            }
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void AppendResourceLimits(StringBuilder builder)
    {
        builder.Append("|limits|").Append(ResourceLimits.MaxFuel);
        builder.Append('|').Append(ResourceLimits.EffectiveWallTime.Ticks);
        builder.Append('|').Append(ResourceLimits.MaxAllocatedBytes);
        builder.Append('|').Append(ResourceLimits.MaxCallDepth).Append('|').Append(ResourceLimits.MaxHostCalls);
        builder.Append('|').Append(ResourceLimits.MaxListLength).Append('|').Append(ResourceLimits.MaxMapEntries);
        builder.Append('|').Append(ResourceLimits.MaxCollectionDepth).Append('|').Append(ResourceLimits.MaxTotalCollectionElements);
        builder.Append('|').Append(ResourceLimits.MaxFileBytesRead).Append('|').Append(ResourceLimits.MaxFileBytesWritten);
        builder.Append('|').Append(ResourceLimits.MaxNetworkBytesRead).Append('|').Append(ResourceLimits.MaxLogEvents);
        builder.Append('|').Append(ResourceLimits.MaxLogMessageLength);
        builder.Append('|').Append(ResourceLimits.MaxStringLength).Append('|').Append(ResourceLimits.MaxTotalStringBytes);
    }
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
    {
        _grants.Add(new CapabilityGrant(capabilityId, ParameterReader.Read(parameters)));
        return this;
    }

    public SandboxPolicyBuilder GrantFileRead(string root, long maxBytesPerRun)
    {
        ThrowIfNegative(maxBytesPerRun, nameof(maxBytesPerRun));
        _allowedEffects |= SandboxEffect.FileRead;
        _grants.Add(new CapabilityGrant("file.read", new Dictionary<string, string> {
            ["root"] = root,
            ["maxBytesPerRun"] = maxBytesPerRun.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        _limits = _limits with { MaxFileBytesRead = Math.Max(_limits.MaxFileBytesRead, maxBytesPerRun) };
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
        _grants.Add(new CapabilityGrant("file.write", new Dictionary<string, string> {
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

    public SandboxPolicyBuilder GrantHttpGet(
        IEnumerable<string> allowedHosts,
        long maxResponseBytes,
        IEnumerable<string>? allowedSchemes = null,
        TimeSpan? timeout = null,
        bool allowIpLiterals = false,
        bool allowPrivateNetwork = false)
    {
        ThrowIfNegative(maxResponseBytes, nameof(maxResponseBytes));
        if (timeout is not null && timeout.Value < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _allowedEffects |= SandboxEffect.Network;
        var schemes = allowedSchemes?.ToArray() ?? ["https"];
        _grants.Add(new CapabilityGrant("net.http.get", new Dictionary<string, string> {
            ["allowedHosts"] = string.Join(',', allowedHosts),
            ["allowedSchemes"] = string.Join(',', schemes),
            ["maxResponseBytes"] = maxResponseBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["timeoutMs"] = ((long)(timeout ?? TimeSpan.FromSeconds(2)).TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allowIpLiterals"] = allowIpLiterals.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allowPrivateNetwork"] = allowPrivateNetwork.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        _limits = _limits with { MaxNetworkBytesRead = Math.Max(_limits.MaxNetworkBytesRead, maxResponseBytes) };
        return this;
    }

    public SandboxPolicyBuilder GrantLogging()
    {
        _allowedEffects |= SandboxEffect.Audit;
        _grants.Add(new CapabilityGrant("log.write", new Dictionary<string, string>()));
        return this;
    }

    public SandboxPolicyBuilder WithFuel(long maxFuel)
    {
        _limits = _limits with { MaxFuel = maxFuel };
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
        return new SandboxPolicy(_policyId, _allowedEffects, _grants, _limits, _deterministic, _logicalNow, _randomSeed);
    }

    private static void ThrowIfNegative(long value, string paramName)
    {
        if (value < 0) {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }
}

internal static class ParameterReader
{
    public static IReadOnlyDictionary<string, string> Read(object parameters)
    {
        if (parameters is IReadOnlyDictionary<string, string> values) {
            return values;
        }

        return parameters.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => Convert.ToString(p.GetValue(parameters), System.Globalization.CultureInfo.InvariantCulture) ?? "");
    }
}
