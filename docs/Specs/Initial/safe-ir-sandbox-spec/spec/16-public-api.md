# 16 — Public C# API

## High-level usage

```csharp
using var sandbox = SandboxHost.Create(builder =>
{
    builder.AddDefaultPureBindings();
    builder.AddFileBindings();
    builder.UseInterpreter();
    builder.UseCompilerIfAvailable();
    builder.UseCompilerCache("/var/cache/safe-ir");
    builder.ForwardAuditEventsTo(audit => auditSink.Write(audit));
    // Optional: builder.UseWorkerClient(workerClient, SandboxWorkerProfile.HardenedOutOfProcess);
});

var module = await sandbox.ImportJsonAsync(jsonIr, cancellationToken);
var hostDataRoot = "/srv/safe-ir";
var tenantId = "123";
var configRoot = Path.GetFullPath(Path.Combine(hostDataRoot, "tenants", tenantId, "config"));
var policy = SandboxPolicyBuilder.Create()
    .AllowPureComputation()
    .GrantFileRead(root: configRoot, maxBytesPerRun: 256_000)
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
public sealed class SandboxHost : IDisposable
{
    public static SandboxHost Create(Action<SandboxHostBuilder>? configure = null);

    public ValueTask<ExecutionPlan> PrepareAsync(
        SandboxModule module,
        SandboxPolicy policy,
        CancellationToken cancellationToken = default);

    public ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    public void RevokeCapability(string capabilityId, string reason = "");
    public void Dispose();
}
```

`ImportJsonAsync` is the extension method provided by the JSON serialization addon.
SafeIR does not expose a custom language parser; hosts import JSON IR into the safe model.
`SandboxHost` owns materialized compiled delegates and should be disposed by long-lived hosts when
the host instance is retired.
`RevokeCapability` is host-local and applies to already-prepared plans. A plan whose module
requests or whose selected entrypoint reaches a revoked capability must fail before interpreted
execution, compiler invocation, or compiled-cache lookup. The failed run emits a
`CapabilityRevoked` audit event containing the safe revocation reason.

### `SandboxHostBuilder`

```csharp
public sealed class SandboxHostBuilder
{
    public SandboxHostBuilder AddDefaultPureBindings();
    public SandboxHostBuilder AddFileBindings();
    public SandboxHostBuilder AddTimeBindings();
    public SandboxHostBuilder AddRandomBindings();
    public SandboxHostBuilder AddLogBindings();
    public SandboxHostBuilder AddBinding(BindingDescriptor descriptor);
    public SandboxHostBuilder UseInterpreter(ISandboxInterpreter? interpreter = null);
    public SandboxHostBuilder UseCompilerIfAvailable(ISandboxCompiler? compiler = null);
    public SandboxHostBuilder UseCompilerCache(string cacheDirectory);
    public SandboxHostBuilder UseExecutionModeSelector(IExecutionModeSelector selector);
    public SandboxHostBuilder ForwardAuditEventsTo(Action<SandboxAuditEvent> observer);
    public SandboxHostBuilder UseWorkerClient(
        ISandboxWorkerClient workerClient,
        SandboxWorkerProfile profile);
}
```

`ForwardAuditEventsTo` registers operational observers. Observer failures are isolated and do not
change the returned `SandboxExecutionResult` or prevent later observers from receiving the same
sequenced audit events.

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

`Interpreted` means direct execution of the verified IR held by the `ExecutionPlan`; it must never
interpret IL, compile to IL, create a `DynamicMethod`, or load a DLL. `Compiled` is the only mode
that may invoke generated IL, and only after the compiler has produced a trusted runtime form such as a gated
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
    public required SandboxResourceUsage ResourceUsage { get; init; }
    public required IReadOnlyList<SandboxAuditEvent> AuditEvents { get; init; }
    public ExecutionMode ActualMode { get; init; }
    public bool ExecutionDispatched { get; init; }
    public required string ModuleHash { get; init; }
    public required string PlanHash { get; init; }
    public required string PolicyHash { get; init; }
    public string? ArtifactHash { get; init; }
}
```

`ActualMode` reports the backend mode for runs that dispatched to a trusted execution backend.
When execution is rejected before dispatch, for example invalid options, deterministic-mode
requirements, revoked capabilities, unavailable worker isolation, or compiled mode with no
compiler and fallback disabled, `ActualMode` retains the requested or effective mode for
correlation and `ExecutionDispatched` is `false`. Fallback results report the backend that actually
ran, so a compiled request that safely falls back to the interpreter has
`ActualMode = Interpreted` and `ExecutionDispatched = true`.
Successful results have `Succeeded = true`, `Value` set, and `Error = null`. Failed results have
`Succeeded = false`, `Value = null`, and a sanitized `SandboxError`.

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
    public SandboxPolicyBuilder GrantFileWrite(
        string root,
        long maxBytesPerRun,
        bool allowCreate = false,
        bool allowOverwrite = false);
    public SandboxPolicyBuilder GrantTimeNow();
    public SandboxPolicyBuilder GrantRandom();
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

### `GrantFileWrite` create and overwrite policy

`GrantFileWrite` exposes two policy-shaping flags beyond `root` and `maxBytesPerRun`, and both
default to safe-by-default deny:

- `allowCreate` (default `false`): when `false`, a granted write that targets a missing file (or that
  would have to create a missing parent directory) is denied. Set it to `true` to permit creating new
  targets under `root`.
- `allowOverwrite` (default `false`): when `false`, a granted write that targets an existing file is
  denied. Set it to `true` to permit replacing existing files under `root`.

These are explicit policy decisions, not implementation details: the two-argument call
`GrantFileWrite(root, maxBytesPerRun)` grants `file.write` but leaves `allowCreate = false` and
`allowOverwrite = false`, so a host that does not opt in will see writes denied at runtime even though
the capability is granted. The builder serializes both flags into the `file.write` grant, and the
runtime fails closed when either flag is absent or `false`.

```csharp
// Create-only grant: new files may be created under the root, but existing files are protected.
var createOnly = SandboxPolicyBuilder.Create()
    .GrantFileWrite(root: outputRoot, maxBytesPerRun: 256_000, allowCreate: true, allowOverwrite: false)
    .Build();

// Overwrite-enabled grant: existing files may be replaced (typically alongside allowCreate).
var overwrite = SandboxPolicyBuilder.Create()
    .GrantFileWrite(root: outputRoot, maxBytesPerRun: 256_000, allowCreate: true, allowOverwrite: true)
    .Build();
```

### `GrantTimeNow` and `GrantRandom` capability grants

Time and random are capability-gated runtime features served by the `SafeIR.Runtime` host bindings
(`SandboxHostBuilder.AddTimeBindings()` and `AddRandomBindings()`). The matching policy-builder
helpers are the intended safe way to authorize a module that declares those capability requests:

- `GrantTimeNow()` grants the `time.now` capability and enables the `Time` effect. Modules that call
  `time.nowUnixMillis` require this grant; without it preparation fails closed with an `E-POLICY-CAP`
  diagnostic.
- `GrantRandom()` grants the `random` capability and enables the `Random` effect. Modules that call
  `random.nextI32` require this grant.

Both helpers are first-class alternatives to the generic `Grant(...)` escape hatch, so deterministic
host setup stays copyable without source or test spelunking.

`Deterministic(logicalNow, randomSeed)` pairs with these grants to make time and random replayable:

- For `time.now`, `LogicalNow` becomes the logical clock. A deterministic run reads `time.now` from
  `LogicalNow` instead of the wall clock, and time binding audit events are stamped with `LogicalNow`.
- For `random`, `RandomSeed` seeds the deterministic generator so the same seed replays the same
  sequence across runs. A deterministic random policy must supply a seed; omitting it fails closed
  with an `E-POLICY-DETERMINISM` diagnostic. Random audit timestamps fall back to `UnixEpoch` when no
  `LogicalNow` is set.

```csharp
using var host = SandboxHost.Create(builder =>
{
    builder.AddDefaultPureBindings();
    builder.AddTimeBindings();
    builder.AddRandomBindings();
});

var policy = SandboxPolicyBuilder.Create()
    .GrantTimeNow()
    .GrantRandom()
    .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 123)
    .Build();
```

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

```csharp
public sealed record CompileOptions(string Entrypoint, bool Optimize = false);
public delegate SandboxValue SandboxCompiledEntrypoint(
    SandboxContext context,
    SandboxValue input);
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

### Compiled cache API

```csharp
public sealed class PersistentCompiledArtifactCache
{
    public PersistentCompiledArtifactCache(string rootDirectory);
    public bool EntryExists(string cacheKey);
    public string EntryPath(string cacheKey);

    public ValueTask<CompiledCacheLookup> TryReadAsync(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        IGeneratedAssemblyVerifier verifier,
        VerificationPolicy policy,
        CancellationToken cancellationToken);

    public ValueTask WriteAsync(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        byte[] assemblyBytes,
        ArtifactManifest manifest,
        VerificationResult verification,
        VerificationPolicy policy,
        CancellationToken cancellationToken);
}

public static class CacheKeyBuilder
{
    public const string CompilerVersion = "safe-ir-compiler-9";
    public const string TypeSystemVersion = "safe-ir-type-system-2";
    public const string EffectAnalysisVersion = "safe-ir-effect-analysis-3";
    public const string CanonicalizerVersion = CanonicalModuleHasher.CanonicalizerVersion;
    public const string TargetFramework = "net10.0";
    public static string LanguageVersion { get; }
    public static string RuntimeFacadeHash { get; }

    public static string Build(
        ExecutionPlan plan,
        string entrypoint,
        VerificationPolicy policy,
        bool optimize);

    public static VerificationManifestIdentity BuildManifestIdentity(
        ExecutionPlan plan,
        string entrypoint,
        VerificationPolicy policy,
        bool optimize);
}

public sealed record CompiledCacheLookup(
    CompiledCacheStatus Status,
    CompiledArtifact? Artifact,
    string? InvalidReason = null);
```

Cache reads validate the cache key, manifest identity, verification metadata, artifact hash, and
fresh verifier result before returning a hit. Invalid entries are quarantined and reported as
`CompiledCacheStatus.Invalid` or `Recompiled` rather than executed.

## JSON addon API

```csharp
public static class SandboxHostJsonExtensions
{
    public static ValueTask<SandboxModule> ImportJsonAsync(
        this SandboxHost host,
        string jsonIr,
        CancellationToken cancellationToken = default);
}

public static class SafeIrJsonImporter
{
    public static SandboxModule Import(string json);
}

public static class SafeIrJsonExporter
{
    public static string Export(SandboxModule module, bool indented = false);
}

public static class PluginPackageJsonSerializer
{
    public static string Export(PluginPackage package, bool indented = false);
    public static PluginPackage Import(string json);
}

public static class PluginServerJsonExtensions
{
    public static ValueTask<InstalledKernel> InstallJsonAsync(
        this PluginServer server,
        string json,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default);
}
```

JSON import is the only built-in text ingestion path for Safe IR. It preserves source locations for
diagnostics and debug traces; it is not a lexer/parser for a custom script language.
Generated plugin package factories return in-memory `PluginPackage` instances for SDK and test
use. Production upload should serialize that package with `PluginPackageJsonSerializer.Export`,
send the JSON envelope, and install it through `InstallJsonAsync`; the envelope contains manifest
metadata plus JSON Safe IR and does not contain assembly loader instructions.

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

public sealed record ModuleHotnessStats(
    string PlanHash,
    string Entrypoint,
    int RunCount,
    int CompletedRunCount,
    TimeSpan AverageInterpretedDuration,
    long AverageFuelUsed,
    DateTimeOffset? LastRunAt,
    int CompileFailures,
    string? LastCompiledArtifactHash);
```

The default selector uses `AutoCompileThreshold`, with a minimum threshold of two so Auto mode never
compiles the first run. `RunCount` is the Auto attempt count including the current selection
attempt; the remaining hotness fields summarize previously completed Auto executions for the same
plan hash and entrypoint. The current host passes `CompiledCacheStatus.None` before compiler/cache
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

## Stabilized minimum surface

The current package set ships JSON IR import, plan preparation, interpreted IR execution,
compiled loaded-assembly execution, cache validation, verifier gates, worker delegation, and the
policy/resource/audit model above. New public APIs should extend this surface without weakening the
same execution boundary: interpreted mode runs IR, and generated IL is invoked only through an
approved compiled runtime form.
