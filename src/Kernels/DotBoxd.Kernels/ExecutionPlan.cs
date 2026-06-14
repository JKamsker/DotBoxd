namespace DotBoxd.Kernels;

using System.Collections.Frozen;

public sealed record FunctionAnalysis(SandboxType ReturnType, SandboxEffect Effects, bool CanReorder);

public sealed class ExecutionPlan
{
    private FrozenDictionary<string, SandboxFunction>? _functionLookup;

    public ExecutionPlan(
        string moduleHash,
        string planHash,
        ExecutionPlanSeal planSeal,
        string policyHash,
        string bindingManifestHash,
        SandboxModule module,
        SandboxPolicy policy,
        BindingRegistry bindings,
        ResourceLimits budget,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? bindingReferences = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHash);
        ArgumentNullException.ThrowIfNull(planSeal);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingManifestHash);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(budget);

        ModuleHash = moduleHash;
        PlanHash = planHash;
        PlanSeal = planSeal;
        PolicyHash = policyHash;
        BindingManifestHash = bindingManifestHash;
        Module = module;
        Policy = policy;
        Bindings = bindings;
        Budget = budget;
        FunctionAnalysis = ModelCopy.Dictionary(functionAnalysis);
        BindingReferences = CopyBindingReferences(bindingReferences ?? BindingReferenceCollector.CollectByFunction(module, bindings));
    }

    public string ModuleHash { get; }
    public string PlanHash { get; }
    public ExecutionPlanSeal PlanSeal { get; }
    public string PolicyHash { get; }
    public string BindingManifestHash { get; }
    public SandboxModule Module { get; }
    public SandboxPolicy Policy { get; }
    public BindingRegistry Bindings { get; }
    public ResourceLimits Budget { get; }
    public IReadOnlyDictionary<string, FunctionAnalysis> FunctionAnalysis { get; }
    public IReadOnlyDictionary<string, IReadOnlySet<string>> BindingReferences { get; }

    // The module function set is immutable for the lifetime of a prepared plan, so the id->function
    // index is built once and reused across every interpreted run instead of being rebuilt per
    // execution. Lazy + Volatile keeps construction race-free without locking on the hot path.
    public IReadOnlyDictionary<string, SandboxFunction> FunctionLookup
        => Volatile.Read(ref _functionLookup) ?? BuildFunctionLookup();

    private FrozenDictionary<string, SandboxFunction> BuildFunctionLookup()
    {
        var lookup = Module.Functions.ToFrozenDictionary(f => f.Id, StringComparer.Ordinal);
        Volatile.Write(ref _functionLookup, lookup);
        return lookup;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> CopyBindingReferences(
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences)
    {
        var copy = new Dictionary<string, IReadOnlySet<string>>(bindingReferences.Count, StringComparer.Ordinal);
        foreach (var item in bindingReferences) {
            copy.Add(item.Key, item.Value.ToFrozenSet(StringComparer.Ordinal));
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlySet<string>>(copy);
    }
}

public sealed class ExecutionPlanSeal : IEquatable<ExecutionPlanSeal>
{
    private readonly string _value;

    public ExecutionPlanSeal(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    public bool Equals(ExecutionPlanSeal? other)
        => other is not null && StringComparer.Ordinal.Equals(_value, other._value);

    public override bool Equals(object? obj) => Equals(obj as ExecutionPlanSeal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);

    public override string ToString() => "[redacted]";
}

public sealed record SandboxExecutionOptions
{
    public ExecutionMode Mode { get; init; } = ExecutionMode.Auto;
    public SandboxIsolation Isolation { get; init; } = SandboxIsolation.InProcess;
    public bool EnableDebugTrace { get; init; }
    public bool AllowFallbackToInterpreter { get; init; } = true;
    public bool RequireDeterministic { get; init; }
    public SandboxRunId? RunId { get; init; }
    public int AutoCompileThreshold { get; init; } = 20;
}

public enum ExecutionMode
{
    Interpreted,
    Compiled,
    Auto
}

public enum SandboxIsolation
{
    InProcess,
    WorkerProcess
}

public sealed record SandboxExecutionResult
{
    private IReadOnlyList<SandboxAuditEvent> _auditEvents = [];

    public bool Succeeded { get; init; }
    public SandboxValue? Value { get; init; }
    public SandboxError? Error { get; init; }
    public required SandboxResourceUsage ResourceUsage { get; init; }
    public required IReadOnlyList<SandboxAuditEvent> AuditEvents
    {
        get => _auditEvents;
        init => _auditEvents = AdoptOrCopy(value);
    }

    // An already-owned, immutable snapshot (for example the one produced on the execution
    // hot path) can be adopted directly; any other input is still defensively copied so
    // external list/array identity never escapes into the public result.
    private static IReadOnlyList<SandboxAuditEvent> AdoptOrCopy(IReadOnlyList<SandboxAuditEvent> value)
        => value is OwnedAuditEventSnapshot owned
            ? owned
            : ModelCopy.List(value);

    public ExecutionMode ActualMode { get; init; }
    public bool ExecutionDispatched { get; init; }
    public required string ModuleHash { get; init; }
    public required string PlanHash { get; init; }
    public required string PolicyHash { get; init; }
    public string? ArtifactHash { get; init; }
}
