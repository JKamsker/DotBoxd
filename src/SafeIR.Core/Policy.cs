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
        builder.Append("|limits|").Append(ResourceLimits.MaxFuel).Append('|').Append(ResourceLimits.MaxAllocatedBytes);

        foreach (var grant in Grants.OrderBy(g => g.Id, StringComparer.Ordinal)) {
            builder.Append("|grant|").Append(grant.Id);
            foreach (var item in grant.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal)) {
                builder.Append('|').Append(item.Key).Append('=').Append(item.Value);
            }
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
        _allowedEffects |= SandboxEffect.FileRead;
        _grants.Add(new CapabilityGrant("file.read", new Dictionary<string, string> {
            ["root"] = root,
            ["maxBytesPerRun"] = maxBytesPerRun.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        _limits = _limits with { MaxFileBytesRead = Math.Max(_limits.MaxFileBytesRead, maxBytesPerRun) };
        return this;
    }

    public SandboxPolicyBuilder GrantFileWrite(string root, long maxBytesPerRun)
    {
        _allowedEffects |= SandboxEffect.FileWrite;
        _grants.Add(new CapabilityGrant("file.write", new Dictionary<string, string> {
            ["root"] = root,
            ["maxBytesPerRun"] = maxBytesPerRun.ToString(System.Globalization.CultureInfo.InvariantCulture)
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

    public SandboxPolicyBuilder WithFuel(long maxFuel)
    {
        _limits = _limits with { MaxFuel = maxFuel };
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

    public SandboxPolicyBuilder Deterministic(DateTimeOffset logicalNow, ulong randomSeed)
    {
        _deterministic = true;
        _logicalNow = logicalNow;
        _randomSeed = randomSeed;
        return this;
    }

    public SandboxPolicy Build() => new(_policyId, _allowedEffects, _grants, _limits, _deterministic, _logicalNow, _randomSeed);
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
