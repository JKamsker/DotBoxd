# 16 — Proposed Public C# API

## High-level usage

```csharp
var sandbox = SandboxHost.Create(builder =>
{
    builder.AddDefaultPureBindings();
    builder.AddFileBindings();
    builder.UseInterpreter();
    builder.UseCompilerIfAvailable();
    // Optional: builder.UseWorkerClient(workerClient, SandboxWorkerProfile.HardenedOutOfProcess);
});

var module = await sandbox.ImportJsonAsync(jsonIr, cancellationToken);
var policy = SandboxPolicyBuilder.Create()
    .AllowPureComputation()
    .GrantFileRead(root: "tenant://123/config", maxBytesPerRun: 256_000)
    .WithFuel(100_000)
    .WithWallTime(TimeSpan.FromMilliseconds(50))
    .Build();

var plan = await sandbox.PrepareAsync(module, policy, cancellationToken);

var result = await sandbox.ExecuteAsync(
    plan,
    entrypoint: "main",
    input: SandboxValue.Unit,
    options: new SandboxExecutionOptions
    {
        Mode = ExecutionMode.Auto
    },
    cancellationToken);
```

## Main abstractions

### `SandboxHost`

```csharp
public sealed class SandboxHost
{
    public ValueTask<ExecutionPlan> PrepareAsync(
        SandboxModule module,
        SandboxPolicy policy,
        CancellationToken cancellationToken);

    public ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken);

    public void RevokeCapability(string capabilityId, string reason = "");
}
```

`ImportJsonAsync` is the preferred extension method provided by the JSON serialization addon.
`ParseJsonAsync` remains a compatibility alias and does not imply a custom language parser.
`RevokeCapability` is host-local and applies to already-prepared plans. A plan whose module
requests or whose selected entrypoint reaches a revoked capability must fail before interpreted
execution, compiler invocation, or compiled-cache lookup. The failed run emits a
`CapabilityRevoked` audit event containing the safe revocation reason.

### `SandboxExecutionOptions`

```csharp
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
```

`Interpreted` means direct execution of the verified IR held by the `ExecutionPlan`; it must not
compile to IL, create a `DynamicMethod`, or load a DLL. `Compiled` is the only mode that may execute
generated IL, and only after the compiler has produced a trusted runtime form such as a gated
`DynamicMethod` or a verified generated assembly. The current host accepts verified loaded assembly
artifacts and rejects `DynamicMethod` artifacts until an equivalent gate exists. `Auto` starts as
interpreted until the hotness threshold promotes a plan to compiled mode.
Hosts may replace the default `IExecutionModeSelector`; the first Auto run is still interpreted
before selector decisions can promote subsequent runs.

`Isolation = WorkerProcess` means the caller requires an out-of-process worker boundary. If the
host has not configured a hardened worker client, execution must fail closed with a policy error
rather than falling back to in-process execution.

### `SandboxExecutionResult`

```csharp
public sealed record SandboxExecutionResult
{
    public bool Succeeded { get; init; }
    public SandboxValue? Value { get; init; }
    public SandboxError? Error { get; init; }
    public SandboxResourceUsage ResourceUsage { get; init; }
    public IReadOnlyList<SandboxAuditEvent> AuditEvents { get; init; }
    public ExecutionMode ActualMode { get; init; }
    public string ModuleHash { get; init; }
    public string PlanHash { get; init; }
    public string PolicyHash { get; init; }
    public string? ArtifactHash { get; init; }
}
```

## Policy builder

```csharp
public sealed class SandboxPolicyBuilder
{
    public SandboxPolicyBuilder AllowPureComputation();
    public SandboxPolicyBuilder Grant(string capabilityId, object parameters);
    public SandboxPolicyBuilder Grant(
        string capabilityId,
        object parameters,
        SandboxEffect allowedEffects,
        Func<ResourceLimits, ResourceLimits>? configureLimits = null);
    public SandboxPolicyBuilder GrantFileRead(string root, long maxBytesPerRun);
    public SandboxPolicyBuilder GrantFileWrite(string root, long maxBytesPerRun);
    public SandboxPolicyBuilder GrantLogging();
    public SandboxPolicyBuilder WithFuel(long maxFuel);
    public SandboxPolicyBuilder WithMaxLoopIterations(long iterations);
    public SandboxPolicyBuilder WithMaxHostCalls(int calls);
    public SandboxPolicyBuilder WithMaxCallDepth(int depth);
    public SandboxPolicyBuilder WithWallTime(TimeSpan maxWallTime);
    public SandboxPolicyBuilder WithMaxAllocatedBytes(long bytes);
    public SandboxPolicyBuilder WithMaxListLength(int length);
    public SandboxPolicyBuilder WithMaxMapEntries(int entries);
    public SandboxPolicyBuilder WithMaxCollectionDepth(int depth);
    public SandboxPolicyBuilder WithMaxTotalCollectionElements(long elements);
    public SandboxPolicyBuilder WithMaxLogEvents(int events);
    public SandboxPolicyBuilder WithMaxLogMessageLength(int length);
    public SandboxPolicyBuilder WithMaxStringLength(int length);
    public SandboxPolicyBuilder WithMaxTotalStringBytes(long bytes);
    public SandboxPolicyBuilder Deterministic(DateTimeOffset logicalNow, ulong randomSeed);
    public SandboxPolicy Build();
}
```

`SandboxResourceUsage` reports fuel, loop iterations, allocation bytes, host calls, file/network bytes,
log events, cumulative collection elements, and string bytes charged during the run.

## Binding registration

```csharp
public sealed class BindingRegistryBuilder
{
    public BindingRegistryBuilder Add(BindingDescriptor descriptor);
    public BindingRegistryBuilder AddRange(IEnumerable<BindingDescriptor> descriptors);
    public BindingRegistry Build();
}
```

Example:

```csharp
builder.Add(new BindingDescriptor(
    Id: "file.readText",
    Version: SemVersion.Parse("1.0.0"),
    Parameters: [SandboxType.SandboxPath],
    ReturnType: SandboxType.String,
    Effects: SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.FileRead,
    RequiredCapability: "file.read",
    CostModel: BindingCostModel.PerByte(baseFuel: 50, perByteFuel: 1),
    AuditLevel: AuditLevel.PerResource,
    Safety: BindingSafety.ReadOnlyExternal,
    Invoke: SafeFileBindings.ReadText.Invoke,
    Compiled: CompiledBinding.RuntimeStub("SafeIR.Runtime.CompiledRuntime", "CallBinding")));
```

## Execution plan

```csharp
public sealed class ExecutionPlan
{
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
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis);

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
}

public sealed class ExecutionPlanSeal
{
    public override string ToString(); // redacted
}
```

## Interpreter API

```csharp
public interface ISandboxInterpreter
{
    ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken);
}
```

## Compiler API

```csharp
public interface ISandboxCompiler
{
    ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken);
}
```

`CompiledArtifact` represents a verified compiled runtime envelope. For `DynamicMethod`, the
artifact contains the already-created delegate. For `LoadedAssembly`, the artifact contains the
verified assembly image and manifest; the host must validate the envelope, verify the assembly
bytes against the manifest/hash, load the assembly into a controlled runtime context, create the
entrypoint delegate from the loaded method, and then invoke that materialized delegate. Hosts must
not interpret raw IL bytes or invoke a stale delegate supplied beside loaded assembly bytes.

```csharp
public enum CompiledRuntimeFormKind
{
    LoadedAssembly,
    DynamicMethod
}

public enum CompiledCacheStatus
{
    None,
    Hit,
    Miss,
    Invalid,
    Recompiled
}

public sealed record CompiledArtifact
{
    public CompiledArtifact(
        byte[] assemblyBytes,
        string assemblyHash,
        ArtifactManifest manifest,
        VerificationResult verification,
        SandboxCompiledEntrypoint entrypoint,
        CompiledRuntimeFormKind runtimeForm,
        CompiledCacheStatus cacheStatus = CompiledCacheStatus.None,
        string? cacheInvalidReason = null);

    public byte[] AssemblyBytes { get; init; }
    public string AssemblyHash { get; init; }
    public ArtifactManifest Manifest { get; init; }
    public VerificationResult Verification { get; init; }
    public CompiledRuntimeFormKind RuntimeForm { get; init; }
    public SandboxCompiledEntrypoint Entrypoint { get; init; }
    public CompiledCacheStatus CacheStatus { get; init; }
    public string? CacheInvalidReason { get; init; }
    public string ArtifactHash { get; }
}
```

`LoadedAssembly` artifacts carry the verified assembly bytes, manifest, and verification result.
`DynamicMethod` artifacts carry only the gated delegate and must not include assembly bytes.

## Verifier API

```csharp
public interface IGeneratedAssemblyVerifier
{
    ValueTask<VerificationResult> VerifyAsync(
        ReadOnlyMemory<byte> assemblyBytes,
        ArtifactManifest manifest,
        VerificationPolicy policy,
        CancellationToken cancellationToken);
}
```

`VerificationPolicy` may include an expected `VerificationManifestIdentity`. Compiler and cache
paths must set it from the current plan, policy, binding manifest, compiler/runtime versions,
target framework, and optimization flags so direct verifier calls reject stale manifests.

## Execution-mode selector

```csharp
public interface IExecutionModeSelector
{
    ExecutionModeDecision Choose(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ModuleHotnessStats hotness,
        CompiledCacheStatus cacheStatus);
}
```

The default selector uses `AutoCompileThreshold`, with a minimum threshold of two so Auto mode never
compiles the first run. The current host passes `CompiledCacheStatus.None` before compiler/cache
lookup; cache hit/miss status is emitted later in `RunSummary`.

## Fallback behavior

Compiled execution is an optimization, not a semantic mode change. If compiled execution is
unavailable, disabled by debug tracing, rejected by the verifier, or rejected by compiled artifact
envelope validation, the host may fall back to interpreted IR execution only when
`AllowFallbackToInterpreter` is true. Fallback must emit an `ExecutionFallback` audit event with the
safe reason code and the final `SandboxExecutionResult.ActualMode` must report the mode that actually
ran. If fallback is disabled, the result remains a compiled-mode failure and must not invoke the
interpreter.

## Worker process API optional

```csharp
public interface ISandboxWorkerClient
{
    ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken);
}

public sealed record SandboxWorkerProfile(
    bool OutOfProcess,
    bool SecretsIsolated,
    bool ResourceLimitsConfigured)
{
    public static SandboxWorkerProfile HardenedOutOfProcess { get; }
}

builder.UseWorkerClient(workerClient, SandboxWorkerProfile.HardenedOutOfProcess);
```

Until a host wires an `ISandboxWorkerClient` with a hardened profile,
`SandboxIsolation.WorkerProcess` is an explicit deny-only mode. Incomplete profiles, such as
in-process clients or workers without resource limits, fail closed without invoking the client.

The host delegates a worker request with `Isolation = InProcess` because the worker process itself
is the required boundary. The host then validates that the returned module, plan, and policy hashes
match the requested execution before publishing the worker result.

## Error model

```csharp
public sealed record SandboxError(
    SandboxErrorCode Code,
    string SafeMessage,
    string? DiagnosticId = null);

public enum SandboxErrorCode
{
    ParseError,
    ValidationError,
    PolicyDenied,
    PermissionDenied,
    NotFound,
    InvalidInput,
    QuotaExceeded,
    Timeout,
    Cancelled,
    BindingFailure,
    VerifierFailure,
    CacheInvalid,
    HostFailure
}
```

## Minimal MVP API

For first implementation, this is enough:

```csharp
public sealed class Sandbox
{
    public PreparedScript PrepareJson(string jsonIr, SandboxPolicy policy);
    public SandboxResult ExecuteInterpreted(PreparedScript script, SandboxValue input);
}
```

Add compiler/cache after the core model is proven.
