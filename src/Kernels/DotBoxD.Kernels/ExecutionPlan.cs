using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels;

using System.Collections.Frozen;

public sealed record FunctionAnalysis(SandboxType ReturnType, SandboxEffect Effects, bool CanReorder);

public sealed class ExecutionPlan
{
    private ExecutionPlanEntrypointMetadata? _moduleOnlyEntrypointMetadata;
    private FrozenDictionary<string, ExecutionPlanEntrypointMetadata>? _entrypointMetadataLookup;
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

    internal ExecutionPlanEntrypointMetadata GetEntrypointMetadata(string entrypoint)
    {
        var lookup = Volatile.Read(ref _entrypointMetadataLookup) ?? BuildEntrypointMetadataLookup();
        return lookup.TryGetValue(entrypoint, out var metadata)
            ? metadata
            : Volatile.Read(ref _moduleOnlyEntrypointMetadata)!;
    }

    private FrozenDictionary<string, SandboxFunction> BuildFunctionLookup()
    {
        var lookup = Module.Functions.ToFrozenDictionary(f => f.Id, StringComparer.Ordinal);
        Volatile.Write(ref _functionLookup, lookup);
        return lookup;
    }

    private FrozenDictionary<string, ExecutionPlanEntrypointMetadata> BuildEntrypointMetadataLookup()
    {
        var moduleCapabilities = ModuleCapabilityIds();
        Volatile.Write(
            ref _moduleOnlyEntrypointMetadata,
            new ExecutionPlanEntrypointMetadata(moduleCapabilities, hasAsyncBinding: false, hasHostBinding: false));

        var metadata = new Dictionary<string, ExecutionPlanEntrypointMetadata>(
            BindingReferences.Count,
            StringComparer.Ordinal);
        foreach (var pair in BindingReferences)
        {
            metadata[pair.Key] = BuildEntrypointMetadata(moduleCapabilities, pair.Value);
        }

        var lookup = metadata.ToFrozenDictionary(StringComparer.Ordinal);
        Volatile.Write(ref _entrypointMetadataLookup, lookup);
        return lookup;
    }

    private string[] ModuleCapabilityIds()
    {
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in Module.CapabilityRequests)
        {
            capabilities.Add(request.Id);
        }

        return capabilities.Count == 0 ? [] : capabilities.ToArray();
    }

    private ExecutionPlanEntrypointMetadata BuildEntrypointMetadata(
        string[] moduleCapabilities,
        IReadOnlySet<string> bindingReferences)
    {
        if (bindingReferences.Count == 0)
        {
            return new ExecutionPlanEntrypointMetadata(
                moduleCapabilities,
                hasAsyncBinding: false,
                hasHostBinding: false);
        }

        var required = new HashSet<string>(moduleCapabilities, StringComparer.Ordinal);
        var hasAsyncBinding = false;
        foreach (var bindingId in bindingReferences)
        {
            if (!Bindings.TryGet(bindingId, out var binding))
            {
                continue;
            }

            if (binding.RequiredCapability is not null)
            {
                required.Add(binding.RequiredCapability);
            }

            if (binding.IsAsync)
            {
                hasAsyncBinding = true;
            }

            if (binding.IsAsync || (binding.Effects & SandboxEffect.Concurrency) != 0)
            {
                required.Add(RuntimeCapabilityIds.Async);
            }
        }

        return new ExecutionPlanEntrypointMetadata(
            required.Count == 0 ? [] : required.ToArray(),
            hasAsyncBinding,
            hasHostBinding: true);
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> CopyBindingReferences(
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences)
    {
        var copy = new Dictionary<string, IReadOnlySet<string>>(bindingReferences.Count, StringComparer.Ordinal);
        foreach (var item in bindingReferences)
        {
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

    /// <summary>
    /// Drops the successful-run <c>RunSummary</c> audit event to avoid its allocation on the hot
    /// no-binding plugin dispatch path. Failure runs always emit their summary regardless. This is
    /// an <b>in-process-only</b> optimization: worker-isolated execution clears it (see
    /// <c>SandboxWorkerExecutor</c>) because worker-result audit validation requires the summary.
    /// Internal because suppressing audit on success is never a supported external contract.
    /// </summary>
    internal bool SuppressSuccessfulRunSummaryAudit { get; init; }
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
