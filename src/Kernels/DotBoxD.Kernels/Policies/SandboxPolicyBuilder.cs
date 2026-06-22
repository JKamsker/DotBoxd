using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
namespace DotBoxD.Kernels.Policies;
using DotBoxD.Kernels;
public sealed class SandboxPolicyBuilder
{
    private readonly List<CapabilityGrant> _grants = [];
    private readonly HashSet<string> _declaredOpaqueIdTypes = new(StringComparer.Ordinal);
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
        var normalizedRoot = NormalizeFileRoot(root, nameof(root));
        _allowedEffects |= SandboxEffect.FileRead;
        _grants.Add(new CapabilityGrant("file.read", new Dictionary<string, string>
        {
            ["root"] = normalizedRoot,
            ["maxBytesPerRun"] = maxBytesPerRun.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        _limits = _limits with { MaxFileBytesRead = maxBytesPerRun };
        return this;
    }
    public SandboxPolicyBuilder GrantFileWrite(
        string root,
        long maxBytesPerRun,
        bool allowCreate = false,
        bool allowOverwrite = false)
    {
        ThrowIfNegative(maxBytesPerRun, nameof(maxBytesPerRun));
        var normalizedRoot = NormalizeFileRoot(root, nameof(root));
        _allowedEffects |= SandboxEffect.FileWrite | SandboxEffect.Audit;
        _grants.Add(new CapabilityGrant("file.write", new Dictionary<string, string>
        {
            ["root"] = normalizedRoot,
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
    public SandboxPolicyBuilder AllowRuntimeAsync()
        => GrantRuntimeAsyncIfMissing();

    public SandboxPolicyBuilder AllowIntraKernelReentrancy()
    {
        _allowedEffects |= SandboxEffect.Concurrency;
        _grants.Add(new CapabilityGrant(RuntimeCapabilityIds.Reentrant, new Dictionary<string, string>()));
        return this;
    }

    public SandboxPolicyBuilder DeclareOpaqueIdType(string name)
    {
        if (!SandboxType.IsWellFormedOpaqueIdName(name))
        {
            throw new ArgumentException(
                "An opaque-id type name must be a well-formed brand identifier that is not a built-in scalar.",
                nameof(name));
        }

        _declaredOpaqueIdTypes.Add(name);
        return this;
    }

    public SandboxPolicyBuilder DeclareOpaqueIdTypes(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
        {
            DeclareOpaqueIdType(name);
        }

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
        ValidateRuntimeCapabilities();
        return new SandboxPolicy(
            _policyId,
            _allowedEffects,
            _grants.ToArray(),
            _limits,
            _deterministic,
            _logicalNow,
            _randomSeed,
            new HashSet<string>(_declaredOpaqueIdTypes, StringComparer.Ordinal));
    }

    private void ValidateRuntimeCapabilities()
    {
        var grantsAsync = false;
        var grantsReentrant = false;
        foreach (var grant in _grants)
        {
            grantsAsync |= string.Equals(grant.Id, RuntimeCapabilityIds.Async, StringComparison.Ordinal);
            grantsReentrant |= string.Equals(grant.Id, RuntimeCapabilityIds.Reentrant, StringComparison.Ordinal);
        }

        if (grantsReentrant)
        {
            throw new InvalidOperationException(
                $"{RuntimeCapabilityIds.Reentrant} is reserved until intra-kernel state isolation ships.");
        }

        if (_deterministic && grantsAsync)
        {
            throw new InvalidOperationException(
                "deterministic policies cannot grant runtime async until serialized async limits are configurable.");
        }
    }

    private SandboxPolicyBuilder GrantRuntimeAsyncIfMissing()
    {
        _allowedEffects |= SandboxEffect.Concurrency;
        for (var i = 0; i < _grants.Count; i++)
        {
            if (string.Equals(_grants[i].Id, RuntimeCapabilityIds.Async, StringComparison.Ordinal))
            {
                return this;
            }
        }

        _grants.Add(new CapabilityGrant(RuntimeCapabilityIds.Async, new Dictionary<string, string>()));
        return this;
    }

    private static void ThrowIfNegative(long value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    private static string NormalizeFileRoot(string root, string paramName)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("file grant root must be an absolute canonical path", paramName);
        }

        var fullPath = Path.GetFullPath(root);
        if (!PathsEqual(NormalizeRootForCompare(root), NormalizeRootForCompare(fullPath)))
        {
            throw new ArgumentException("file grant root must be an absolute canonical path", paramName);
        }

        return fullPath;
    }

    private static string NormalizeRootForCompare(string path)
        => Path.TrimEndingDirectorySeparator(path);

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
