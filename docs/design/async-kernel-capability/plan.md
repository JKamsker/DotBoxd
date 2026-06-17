# FINAL PLAN: True Async/Await in Kernels as a Gated Capability

> Synthesized from a multi-agent planning pass (4 Claude design lenses + 1 Codex
> independent plan, reconciled by adversarial architecture + security critiques).
> Resolves #31 (compiled sync-over-async deadlock / threadpool-blocking). Informs
> and scopes #30 (per-kernel gate held across the await). Async is OFF by default,
> server-granted only, fail-closed.

## 1. Decision summary

- **Execution path:** Keep the synchronous compiled IL and entrypoint exactly as-is; move the async boundary into the trusted runtime facade via a thread-static await pump driving the unchanged sync IL on a dedicated single-threaded worker (the "trampoline"). For genuine thread-freedom, route async-heavy kernels to the already-truly-async interpreter and do not auto-promote them to compiled. *Rejected:* emitting async IL / changing `SandboxCompiledEntrypoint` to return `ValueTask` (codex, migration PR5, concurrency-gate Phase 2 fallback) — the architecture critique independently verified this is unbuildable against the current verifier (`OpCodeVerifier.cs:44-46` `V-EXCEPTION` rejects all exception regions; `VerificationPolicy.cs:206-208` bans `System.Threading.Tasks.`; `AllowedMembers` is a closed allowlist returning only `SandboxValue`/scalars; `GeneratedExecuteShapeVerifier` requires a branch-free direct-return `Execute`).
- **Capability model:** Two flat, no-parameter capabilities mirroring `time.now`/`random`: `dotboxd.runtime.async` (gates async tail; default serialized) and `dotboxd.runtime.reentrant` (gates #30; requires async as prerequisite; **not grantable until its isolation machinery ships**). Analyzer-derived requirement from a new `BindingDescriptor.IsAsync` flag folded into the manifest hash. *Rejected:* single combined capability (loses the ability to fix #31 without opening #30); grant parameters in v1 (use `ResourceLimits` if ever needed).
- **Gate / concurrency (#30):** Keep `_executionGate` held across the now-truly-async tail by default (await parks the logical op without pinning a pool thread — this alone fixes #31). First throughput lever is horizontal scaling (N serialized kernel instances). Intra-kernel reentrancy is a deferred, separately-gated, opt-in feature requiring shape-keyed per-execution state isolation. *Rejected:* release/reacquire `_executionGate` around `ExecutePreparedAsync` (codex step 6) — both critiques confirm it corrupts the three shared per-kernel singletons (`_preparedInputValues`/`_preparedInputList`, the reused `CompiledNoAuditRunState` `SandboxContext`+`ResourceMeter`, live-settings cache), a silent budget/capability-enforcement hole plus the PAL-0046 aliasing bug on the primary path.
- **Deny behavior:** Hard-fail with `PermissionDenied`/`BindingFailure` on any ungranted async. *Rejected:* keeping the legacy blocking `AwaitBinding` for ungranted kernels (async-execution-path's original deny path) — the architecture critique flags this as a fail-closed violation (ungranted plugin silently gets the #31 deadlock). Synchronous bindings that complete inline (`IsCompletedSuccessfully`) are not "async behavior" and remain ungated.
- **Rollout:** migration-testing's phased PR plan, with its PR5 rewritten from "emit async IL" to "trampoline in the trusted facade."

## 2. Capability design

### Names and shape

Two capability id constants (flat, lowercase, dotted-namespace, matched by existing `CapabilityPattern.Matches` with zero changes — `*` and `dotboxd.runtime.*` both match):

- `dotboxd.runtime.async` — authorization to run a genuinely-pending async binding (the async tail). Kernel stays serialized.
- `dotboxd.runtime.reentrant` — authorization for intra-kernel concurrency (#30). Requires `dotboxd.runtime.async`. Blocked at install until the isolation machinery (§4 Phase 2) ships.

Add one effect bit in the free range between `HostStateWrite=1<<8` and `Audit=1<<11`:

```csharp
// SandboxEffect.cs
Concurrency = 1 << 9,
```

Add `Concurrency` to the `allKnown` mask in `ContainsOnlyKnownBits`. **Do not** add it to any base-exempt/`RequiresCapability` set. **Critical (architecture gap #1 / security Vector 6):** verify the new bit only ever appears in a module's effect set when an `IsAsync` binding is actually referenced, so it does **not** perturb effect-set serialization / `ModuleHash` / `PlanHash` / `DBXK041` manifest-effect parity for existing sync modules (which would silently invalidate every cached compiled artifact).

### Manifest declaration

`PluginManifest.RequiredCapabilities` is **analyzer-derived, never self-asserted** (closes smuggling Vector 4). The analyzer adds `"dotboxd.runtime.async"` whenever the IR references a binding whose host-registered `BindingDescriptor.IsAsync == true`.

```json
{ "requiredCapabilities": ["dotboxd.runtime.async"] }
```

### Server grant API (concrete signatures)

In `src/Kernels/DotBoxD.Kernels/Policies/SandboxPolicyBuilder.cs`, mirroring `GrantRandom()`/`GrantLogging()`:

```csharp
public SandboxPolicyBuilder AllowRuntimeAsync()
{
    _allowedEffects |= SandboxEffect.Concurrency;
    _grants.Add(new CapabilityGrant("dotboxd.runtime.async", new Dictionary<string, string>()));
    return this;
}

// Phase 2 only; throws at Build() until the isolation machinery is wired.
public SandboxPolicyBuilder AllowIntraKernelReentrancy()
{
    _allowedEffects |= SandboxEffect.Concurrency;
    _grants.Add(new CapabilityGrant("dotboxd.runtime.reentrant", new Dictionary<string, string>()));
    return this;
}
```

### Install-time validation + diagnostics

Falls out of existing machinery in `PolicyResolver.Validate` (`PolicyResolver.cs:40-49`):

- `E-POLICY-CAP` — `dotboxd.runtime.async` in analyzer-derived `requiredCapabilities` but not granted.
- `E-POLICY-EFFECT` — `Concurrency` in `requiredEffects` but not in `AllowedEffects`.
- `E-POLICY-GRANT-PARAM` — extend the no-parameter case in `PolicyGrantValidator.ValidateGrant` to `"time.now" or "random" or "log.write" or "dotboxd.runtime.async" or "dotboxd.runtime.reentrant"`; any stray parameters reject.
- `E-POLICY-DETERMINISM` (new) — `Deterministic = true` policy that grants async is rejected at `Build()`/validate unless serialized (see ExecutionMode matrix). Concurrent awaits contradict deterministic RNG/audit ordering.
- `DBXK043` (new, plugin layer) in `PluginPreparedPackageValidator` + `PluginDiagnosticCodes` (audience `HostOperator`): "plugin requires async but policy does not grant `dotboxd.runtime.async`." Remediation: call `AllowRuntimeAsync()` or remove the async binding.
- Reject install if compiled/cached metadata says async-required but the analyzer-derived requirement disagrees (stale-artifact cross-check).
- Reject `dotboxd.runtime.reentrant` outright until Phase 2 ships (security checklist; negative test T-NEG-007).

**Security action (Vector 4):** verify the call site of `PolicyResolver.Validate` on the plugin install path passes the **analyzer-computed** required-capability set, not `module.CapabilityRequests`/`PluginManifest.RequiredCapabilities` alone.

### Runtime fail-closed enforcement (defense-in-depth, all three layers mandatory)

Single source of truth on `SandboxContext`:

```csharp
public bool AsyncEnabled => Policy.GrantsCapability("dotboxd.runtime.async");
```

Policy-derived (cannot be spoofed by execution options), O(1) indexed probe.

- **Layer 1 (prepare):** install rejection above.
- **Layer 2 (runtime backstop):** in **both** `CompiledBindingDispatcher.CallBinding`/`CallBinding2` (**including the `ITwoArgumentBindingInvoker` fast-invoker branch at `CompiledBindingDispatcher.cs:94-96`** — architecture gap #2, a missed branch is a silent bypass) and `InterpreterBindingCaller.CallAsync`, immediately after `context.ChargeBindingCall(descriptor)`:

```csharp
if (descriptor.IsAsync && !context.AsyncEnabled)
{
    context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, SandboxErrorCode.PermissionDenied);
    throw new SandboxRuntimeException(new SandboxError(
        SandboxErrorCode.PermissionDenied,
        $"binding '{descriptor.Id}' requires the 'dotboxd.runtime.async' capability"));
}
```

This is **separate from** the `RequiredCapability` path (security Gap A) — do not collapse them; they catch different failure modes (honest install vs tampered package vs sync-binding smuggle).

- **Layer 3 (pre-execution revocation):** `SandboxHost.TryGetRevokedCapability` already enumerates `RequiredCapabilities(plan, entrypoint)`; because every async binding carries `RequiredCapability = "dotboxd.runtime.async"`, revocation fires before execution (security Gap B — verify the binding-`RequiredCapability` path, not a manifest-only `CapabilityRequest`).
- **Routing gate (optimization, NOT the security gate):** the trampoline runner is selected in `SandboxHost.PreparedValue.cs` only when `plan.Policy.GrantsCapability("dotboxd.runtime.async") && allowedBindings.Count > 0`. Never the sole enforcement point (security Gap C).

### Revocation semantics

`SandboxHost.RevokeCapability("dotboxd.runtime.async", reason)` denies before execution via Layer 3. For an in-flight await: the wall-time token (`CreateWallTimeToken`) is linked through `PluginExecutionCancellation.Create(cancellationToken, _revocation.Token)` → `SandboxContext.CancellationToken`, so `Revoke()` cancels the pending binding (security Hazard R1 — **verify `PluginExecutionCancellation.Create` actually links the revocation token**). After the pump returns, the existing `IsRevoked` check at `InstalledKernel.Execution.cs:30-33` throws. Capture the grant clock once per binding call and reuse it for all capability checks within that call (security Risk D3 — closes the TOCTOU where a grant expires between `ChargeBindingCall` and the post-await success audit).

### ExecutionMode interaction matrix

| Policy grants async? | Plugin uses async binding? | Interpreter | Compiled | Auto |
|---|---|---|---|---|
| No | No | Runs (sync fast-paths) | Runs (sync entrypoint, unchanged) | Promotes normally |
| No | Yes | **Prepare-reject** (`E-POLICY-CAP`+`DBXK043`); runtime backstop `BindingFailure` | **Prepare-reject**; runtime backstop | **Prepare-reject** |
| Yes | No | Runs sync; async path never entered | Runs sync entrypoint; no worker spun up | Promotes normally |
| Yes | Yes | Runs truly async (thread-free) | Runs via trampoline (non-blocking, parked dedicated thread) | Prefer interpreter; **do not auto-promote async-binding-heavy kernels** to compiled (architecture gap #6 — promotion would be a thread-scalability regression) |

Deterministic policies may grant async only if serialized (`MaxConcurrentAwaits == 1`); otherwise `E-POLICY-DETERMINISM`.

## 3. Execution-path changes

### Contracts — what stays, what's added

**Unchanged (this is the design's headline virtue — zero churn on the most fragile baselined surface, `DotBoxD.Kernels.Compiler.txt`):**

```csharp
// CompilerContracts.cs — UNCHANGED
public delegate SandboxValue SandboxCompiledEntrypoint(SandboxContext context, SandboxValue input);
```

No emitter change, no verifier change, no `CompiledArtifact` field, no `CompiledArtifactGuard.EnsureEntrypointSignature` change, no compiler-version bump for sync kernels.

**Added — `BindingDescriptor` / `BindingSignature` gain `IsAsync`:**

```csharp
// BindingContracts.cs
public bool IsAsync { get; init; } = false;   // host-declared at registration
```

Fold `IsAsync` into `BindingRecord` → `ManifestHash` so flipping async-ness changes the manifest identity (closes smuggling Vectors T2/T3). `BindingInvoker` (`ValueTask<SandboxValue>`) is unchanged — sync bindings keep returning completed `ValueTask`s.

### Dispatcher — the trampoline (trusted facade C#, never verified IL)

`CompiledBindingDispatcher.AwaitBinding` (`CompiledBindingDispatcher.cs:136-147`) — the sole blocking point — becomes:

```csharp
[ThreadStatic] private static ICompiledAwaitPump? _pump;  // set only on the worker thread

private static SandboxValue AwaitBinding(SandboxContext context, ValueTask<SandboxValue> pending)
{
    if (pending.IsCompletedSuccessfully) return pending.Result;        // hot sync path — zero overhead, unchanged
    if (!context.AsyncEnabled)
        throw new SandboxRuntimeException(new SandboxError(             // Vector 1 / T4 close — CRITICAL
            SandboxErrorCode.BindingFailure,
            "binding returned a pending result; async capability is not granted"));
    var pump = _pump;
    if (pump is null)
        throw new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.BindingFailure, "async pump not installed"));
    return pump.RunToCompletion(pending);   // parks the dedicated worker; never blocks a pool thread
}
```

The pending-without-grant throw is the **critical security close** (security Vector 1, severity HIGH): a sync-declared binding (`IsAsync=false`, no `RequiredCapability`) that returns a genuinely-pending `ValueTask` must `BindingFailure` immediately — never block, never pump.

**Interpreter parity (security Vector 1, interpreter half):** in `InterpreterBindingCaller.CallAsync`, check `IsCompletedSuccessfully` **before** the await; if pending and `!context.AsyncEnabled`, throw `BindingFailure` before awaiting. This is the only watertight close on the interpreter side (a post-hoc check cannot detect that the continuation already escaped).

### Runner — surface suspension to the host

`CompiledExecutionRunner.ExecuteAsync` keeps its current synchronous body as the legacy/sync path. Add `ExecuteOnWorkerAsync`, selected by the routing gate (§2):

1. Rent a pooled `CompiledAsyncWorker` (one dedicated thread, its own single-threaded `SynchronizationContext`, its own pump queue).
2. Set `_pump` on that thread; call `context.RequireCapability("dotboxd.runtime.async")` (defense in depth).
3. Invoke the **unchanged** `artifact.Entrypoint(context, input)` on the worker.
4. Return `ValueTask<SandboxExecutionResult>` completing when the worker finishes. The host `await`s it — `SandboxHost.ExecutePreparedValueInProcessAsync` → `InstalledKernel.ExecutePreparedAsync` is now genuinely async; no host thread is blocked during binding I/O.

`SandboxContext`/audit/budget objects are created exactly as today (`CompiledExecutionRunner.cs:31-45`); only the delegate invocation moves onto the worker.

### Sync fast-path stays zero-overhead

- `IsCompletedSuccessfully` returns inline on the worker thread, identical to today — no state machine, no extra allocation.
- The `CanUseNoAuditSuccessPath` / `CompiledNoAuditValueRunner` reuse path (zero-binding ShouldHandle, `allowedBindings.Count == 0`) can never reach a binding, can never be async, and is gated out of the trampoline by `allowedBindings.Count > 0` (security Vector 6 — confirmed closed by `ReusableNoAuditState`'s existing null-return condition). The `_preparedValueState` singleton and `ReusableNoAuditState` are byte-for-byte untouched.

### Cancellation / SynchronizationContext

- Cancellation flows unchanged through `SandboxContext.CancellationToken` + `CreateWallTimeToken()` into `descriptor.Invoke(..., timeout.Token)`; the existing `catch (OperationCanceledException)` arms (`CompiledBindingDispatcher.cs:41-57`) produce identical audit/error to the interpreter (`InterpreterBindingCaller.cs:58-73`). Parity is preserved because the catch/audit logic is untouched.
- The dedicated worker installs its **own** single-threaded `SynchronizationContext`; binding continuations marshal back to the worker via `ConfigureAwait(true)`, preserving single-threaded determinism *inside* the run. An ambient ASP.NET/UI `SynchronizationContext` on the caller is never captured onto a pool thread — eliminating the #31 deadlock class.
- **Riskiest invariant (continuation affinity):** all `SandboxContext` mutation (audit, RNG, return-credit, `ChargeBindingReturn`) happens in `CallBinding` **after** `AwaitBinding` returns on the worker, never inside arbitrary binding bodies. A deliberately `ConfigureAwait(false)`-internal async binding must be in the parity suite to prove a binding cannot corrupt audit/budget ordering.
- **DoS invariant:** every async binding MUST be wall-time bounded (`CreateWallTimeToken`); a never-completing binding otherwise parks a dedicated thread indefinitely (architecture gap #8). Bound live workers to ≤ concurrent gated kernels.

## 4. Concurrency / gate decision (#30)

**Now (ships with #31):** keep `_executionGate` held across the truly-async (parked) tail — no acquire/hold/release change in `InstalledKernel.cs`. An `await` inside the held gate parks without pinning a pool thread, fixing #31 while preserving every single-threaded invariant (deterministic RNG, ShouldHandle→Handle atomicity, ordered audit, return-credits, live-settings sync, reused-scratch safety). The gate is **load-bearing for memory safety** (both critiques confirm: it is the sole mutual exclusion for `_preparedInputValues`/`_preparedInputList`, the reused `CompiledNoAuditRunState` `SandboxContext`+`ResourceMeter`, and the live-settings cache).

**First #30 throughput lever (defer, cheap):** horizontal scaling — N `InstalledKernel` instances per logical plugin (per shard/owner). `KernelRegistry` already keys by owner; `PluginSession` already manages multi-kernel lifetime. Each instance stays serialized; aggregate I/O throughput scales by instance count. Add a sharded dispatch helper hashing events to one of N instances.

**Last resort (defer, expensive, separately gated):** intra-kernel reentrancy behind `dotboxd.runtime.reentrant`, only if horizontal scaling is proven insufficient. Requires eliminating the shared singletons via shape-keyed per-execution pooling (generalize `SnapshotInput` into the rent path; pool `CompiledNoAuditRunState` per in-flight execution), serialized pre-section (live-settings sync, RNG sub-seed assignment, input rent+snapshot, ShouldHandle→Handle pairing) → unguarded tail on rented non-shared state → serialized post-section (audit commit, return-credit settlement, live-update flush). RNG reproducibility via deterministically-derived sub-seeds assigned in the serialized pre-section. **Never** via release/reacquire around the current shared singletons.

**Hard guard until Phase 2 ships:** `ReusableNoAuditState` must never reuse the singleton when reentrancy is granted, and `dotboxd.runtime.reentrant` must be un-grantable (reject at install). Assert both (security checklist; T-NEG-007).

## 5. Threat model & fail-closed checklist

| # | Vector | Close | Severity |
|---|---|---|---|
| V1 | Sync-declared binding returns genuinely-pending `ValueTask` | `AwaitBinding`: `!IsCompletedSuccessfully && !AsyncEnabled` ⇒ throw `BindingFailure` (never block/pump). Interpreter: check before await. | CRITICAL |
| V4 | Manifest strips `RequiredCapabilities` to hide async | `RequiredCapabilities` analyzer-derived from `IsAsync` over verified IR, not from manifest; mismatch is a `DBXK041`-style reject. Verify call site passes analyzer set. | — |
| T2 | Tampered package: async binding with `RequiredCapability` stripped, `IsAsync=true` | Layer-2 `IsAsync && !AsyncEnabled` backstop reads `descriptor.IsAsync` from host `BindingRegistry`, not the package. | — |
| T3 | Registry flips a binding's `IsAsync` | `IsAsync` folded into `BindingRecord` → `ManifestHash`; mismatch fails identity. | — |
| T6/V6 wildcard | `*` self-grant for async | Grants come only from host policy, never the plugin; `*` matching `dotboxd.runtime.async` is an explicit host decision. | LOW |
| R1 | Escaped continuation after revoke | Wall-time token linked through `_revocation.Token`; `IsRevoked` check after pump. Verify `PluginExecutionCancellation.Create` links revocation. | MEDIUM |
| R2 | PAL-0046 aliasing across async suspend | Gate held across tail (no reentrancy until Phase 2); `ReusableNoAuditState` never reused when reentrant; defensive `SnapshotInput`. | CRITICAL if reentrancy enabled early |
| D3 | Grant expiry TOCTOU between suspend/resume | Capture grant clock once per binding call; reuse for all checks in that scope. | HIGH |
| Auto-promote | I/O kernel promoted to trampoline = thread regression | Do not auto-promote async-binding-heavy kernels; prefer interpreter. | MEDIUM |
| Fallback | `AllowFallbackToInterpreter` after partial compiled run | If any binding already audited in compiled path, fallback result must not silently succeed; mark partial. | MEDIUM |

**Fail-closed checklist (all must hold):** analyzer derives `dotboxd.runtime.async` for any `IsAsync` reference regardless of manifest; `E-POLICY-CAP`/`E-POLICY-GRANT-PARAM`/`E-POLICY-DETERMINISM` fire; `IsAsync` in `ManifestHash`; `AwaitBinding` throws on pending-ungated in **both** dispatchers (incl. `CallBinding2`/`ITwoArgumentBindingInvoker`); trampoline selected only when granted **and** `allowedBindings.Count > 0`; `_executionGate` held across the tail (no release/reacquire); `dotboxd.runtime.reentrant` un-grantable pre-Phase-2; audit events in IL-execution order on the worker thread; deterministic+async only if serialized.

**Required negative tests:** T-NEG-001 install denial without grant; T-NEG-002 wildcard `*` authorizes async; T-NEG-003 sync binding returns pending `ValueTask` (compiled + interpreted) ⇒ immediate `BindingFailure`; T-NEG-004 manifest-strip re-derivation reject; T-NEG-005 revoke mid-await; T-NEG-006 SynchronizationContext deadlock repro (granted: completes <5s; ungranted: immediate `BindingFailure`); T-NEG-007 reentrancy un-grantable; T-NEG-008 deterministic rejects async unless serialized; T-NEG-009 grant expiry mid-execution; T-NEG-010 compiled vs interpreted produce byte-identical deny.

## 6. Test & parity plan

**Invariant:** granted ⇒ compiled and interpreted observably identical (result, `Succeeded`, `Error.Code`, ordered `AuditEvents`, sink contents/order, `ResourceUsage.HostCalls`/fuel within existing tolerance). Ungranted ⇒ both fail closed identically.

New parity classes in `tests/DotBoxD.Kernels.Tests/Compiled/SideEffectParity/` (follow `Compiled*ParityTests` convention, `AllowFallbackToInterpreter=false`):

- `CompiledAsyncSynchronizationContextParityTests.cs` — **the #31 gate.** Single-threaded pumping `SynchronizationContext` + a binding that yields; hard `Task.WhenAny(run, Task.Delay(5s))` timeout = deadlock = fail; compiled == interpreted; ungated ⇒ `BindingFailure`. **Add to `run-required-tests.ps1` allowlist.**
- `CompiledAsyncCancellationParityTests.cs` — pre-cancel, mid-await cancel, wall-time timeout mid-await.
- `CompiledAsyncCapabilityParityTests.cs` — granted/denied × interpreted/compiled 2×2.
- `CompiledAsyncRevocationParityTests.cs` — revoke mid-await.
- `CompiledAsyncSinkOrderingParityTests.cs` — multi-interleaved-await ordered sink + audit (extends existing `CompiledSideEffectAsyncSinkParityTests`).
- `tests/DotBoxD.Kernels.Tests/Fuzz/AsyncDifferentialFuzzTests.cs` — random async-binding modules, interpreter-vs-compiled value+audit-order+`HostCalls` parity over seeds (extends `DifferentialFuzzTests`).

Regression (new `ASY` area): `Fix_ASY_0001` (#31 no-deadlock, granted), `Fix_ASY_0002` (#31 ungranted async rejected at prepare), `Fix_ASY_0003` (#30 overlap when reentrant ON, serialized when OFF). Plugin: `Plugins/Capability/AsyncCapabilityGatingInstallTests.cs` (mirror `CapabilityGatingInstallTests`); extend `Fix_PAL_0046`-style snapshot isolation across the async boundary (re-run under the new async tail to prove no aliasing across the await — architecture gap #4).

Benchmarks (`benchmarks/DotBoxD.Kernels.Benchmarks/`, `*Probe.cs` + `--probe-*`, record in `BENCHMARK_HISTORY.md`): `CompiledAsyncFastPathProbe` (~0 extra alloc on `IsCompletedSuccessfully` — proves sync plugins unaffected; **the key backward-compat gate**); `CompiledAsyncThroughputProbe` (yielding binding: no pool blocking, report threads); `KernelConcurrencyProbe` (OFF == today's serialized baseline; ON approaches horizontal baseline for I/O).

API baselines (`docs/api-baselines/*.txt`, additive-only, same PR via `check-api-compat-baseline.ps1 -Update`): `DotBoxD.Kernels.txt` (`SandboxEffect.Concurrency`, `AllowRuntimeAsync()`, `SandboxContext.AsyncEnabled`); `DotBoxD.Kernels.Runtime.txt` / `DotBoxD.Kernels.Compiler.txt` — **untouched** (trampoline adds no public delegate/field — the major advantage over dual-entrypoint). `DotBoxD.Plugins.txt` (`IsAsync`, `DBXK043`).

## 7. Sequenced PR-sized build order

Each PR ends green (`dotnet build DotBoxD.slnx -c Release` + per-project `dotnet test` + `gates`). Async OFF and safe at every intermediate commit.

1. **PR1 — Capability surface, default-deny (no behavior change).** `SandboxEffect.Concurrency` + `ContainsOnlyKnownBits`; `AllowRuntimeAsync()`; `SandboxContext.AsyncEnabled`; id constants. Update `DotBoxD.Kernels.txt`. *Gate:* full suite green; `AsyncCapabilityGatingInstallTests` proves grant/deny shape; no existing behavior changes; confirm `SandboxEffect.Concurrency` does not perturb existing `ModuleHash`/`DBXK041`.
2. **PR2 — `BindingDescriptor.IsAsync` + manifest hash.** Add `IsAsync` to `BindingSignature`/`BindingDescriptor`/`BindingRecord`. *Gate:* manifest hash changes when `IsAsync` flips (T3).
3. **PR3 — Analyzer derivation + fail-closed prepare.** `FunctionAnalyzer` adds `Concurrency` effect + `"dotboxd.runtime.async"` for `IsAsync` references; SDK analyzer unions into manifest; `PolicyGrantValidator` no-param case; `DBXK043`; `E-POLICY-DETERMINISM` guard. *Gate:* `Fix_ASY_0002`, T-NEG-001/002/004/008; sync modules derive nothing. **(#31 prep, #30 prep — fail-closed.)**
4. **PR4 — Runtime backstop (parity).** `IsAsync && !AsyncEnabled` hard-fail + pending-ungated `BindingFailure` in **both** `CompiledBindingDispatcher` (`CallBinding`/`CallBinding2`/`ITwoArgumentBindingInvoker`) and `InterpreterBindingCaller`; grant-clock capture per call. *Gate:* T-NEG-003/009/010; differential parity green (runtime still synchronous). **(Closes V1/D3.)**
5. **PR5 (rewritten — the #31 fix) — trampoline in the trusted facade.** Add `CompiledAsyncWorker` + `ICompiledAwaitPump` + thread-static `_pump`; the 4-line `AwaitBinding` branch; `CompiledExecutionRunner.ExecuteOnWorkerAsync`; routing gate in `SandboxHost.PreparedValue.cs` (`GrantsCapability("dotboxd.runtime.async") && allowedBindings.Count > 0`). **No verifier/emitter/contract/cache-key change.** *Gate (critical):* `CompiledAsyncSynchronizationContextParityTests` green (no deadlock, compiled==interpreted), `AsyncDifferentialFuzzTests` green, `CompiledAsyncFastPathProbe` ~0 extra alloc, `Fix_ASY_0001` green; add SyncContext deadlock test to `run-required-tests.ps1`. **(Resolves #31.)**
6. **PR6 — Cancellation / revocation / capability parity (compiled).** Land the four async parity classes; verify `PluginExecutionCancellation.Create` links revocation (R1); wire `CapabilityViolationResponse` (default `DenyCall`) as Layer-2 backstop. *Gate:* T-NEG-005/006; all four parity classes green both backends. **(Hardens #31.)**
7. **PR7 — #30 lever: horizontal scaling.** Document/support N serialized kernel instances; sharded dispatch helper. *Gate:* `KernelConcurrencyProbe` shows ~linear I/O scaling with instance count; each instance retains single-threaded determinism. **(Informs #30, cheap.)**
8. **PR8 (optional, gated, only if PR7 insufficient) — intra-kernel reentrancy.** Shape-keyed per-execution pooling (generalize `SnapshotInput`, pool `CompiledNoAuditRunState`), serialized pre/post sections + unguarded tail, behind `dotboxd.runtime.reentrant` (requires async). *Gate:* `Fix_ASY_0003` (overlap ON, serialized OFF), extended PAL-0046 isolation under reentrancy, per-execution budget/RNG independence, `KernelConcurrencyProbe` OFF==baseline. **(Resolves #30 if pursued.)**
9. **PR9 — Docs + benchmark history + CI wiring.** Update `capability-gating.md`, `docs/concepts/runtime.md`/`kernels.md`, `docs/security/sandbox-caveats.md`; append `BENCHMARK_HISTORY.md`; confirm required-tests allowlist + baseline gate.

## 8. Risks & open questions

**Top risks (with mitigations):**

1. *Trampoline is non-blocking-but-thread-parked, not thread-free for compiled.* It fixes #31's deadlock/pool-starvation but trades a pool thread for a dedicated thread; no thread-scalability win for compiled. Mitigation: route I/O-heavy async kernels to the truly-thread-free interpreter and do not auto-promote; bound workers ≤ concurrent gated kernels.
2. *Continuation affinity corruption (parity).* A binding using `ConfigureAwait(false)` then touching `SandboxContext` could run a continuation on a foreign thread. Mitigation: all metering/audit in `CallBinding` after `AwaitBinding` returns on the worker; deliberate `ConfigureAwait(false)`-internal binding in the parity suite.
3. *`SandboxEffect.Concurrency` perturbing hashes.* Could invalidate all cached artifacts / break `DBXK041`. Mitigation: bit appears only when an `IsAsync` binding is referenced; verify serialization boundary in PR1.
4. *Premature reentrancy.* Enabling #30 before state isolation corrupts singletons (capability-enforcement hole). Mitigation: `dotboxd.runtime.reentrant` un-grantable until PR8; assert `ReusableNoAuditState` never reused when reentrant.
5. *Never-completing async binding DoS.* Parks a dedicated thread. Mitigation: every async binding wall-time bounded; bounded worker pool.

**Questions needing a human decision:**

- Final capability id: `dotboxd.runtime.async` (codex) vs `concurrency` (claude) — bikeshed; pick and baseline. (Plan uses `dotboxd.runtime.async`.)
- Is the trampoline (parked dedicated thread) an acceptable compiled-mode #31 fix, or must compiled async-heavy kernels hard-route to the interpreter (giving up compiled for those)?
- `CapabilityViolationResponse` default for async violation: `DenyCall` vs `DisconnectPlugin`?
- Should `Auto` mode formally exclude async-binding-heavy kernels from compiled promotion, or only down-weight hotness?
- Is #30 (PR7 horizontal scaling, PR8 reentrancy) in scope for this milestone, or deferred entirely?

## Critical files for implementation

- `src/Kernels/DotBoxD.Kernels.Runtime/CompiledBindingDispatcher.cs`
- `src/Hosting/DotBoxD.Hosting/Execution/CompiledExecutionRunner.cs`
- `src/Kernels/DotBoxD.Kernels/Policies/SandboxPolicyBuilder.cs`
- `src/Kernels/DotBoxD.Kernels/Bindings/BindingContracts.cs`
- `src/Hosting/DotBoxD.Plugins/Kernel/InstalledKernel.cs`

---

## Appendix: how this plan was produced

Multi-agent planning pass (Codex + Claude), reconciled by adversarial review:

- **Understand** — 6 parallel `Explore` agents mapped dispatch/gate, compiled dispatch, interpreter path, capability gating, descriptors/effects, and execution-mode/tests (5/6 maps succeeded; descriptors/effects map failed its structured-output call but was covered by the design-phase agents reading source directly).
- **Design** — 4 Claude `Plan` lenses (async execution path, capability gating, concurrency gate, migration/testing) + 1 Codex independent end-to-end plan.
- **Critique** — adversarial architecture review + a security review hunting fail-closed/smuggling holes.
- **Key reconciliation:** Codex and two Claude lenses proposed emitting async IL / returning `ValueTask` from the compiled entrypoint. The architecture critique verified against verifier source that this is **unbuildable** (exception regions banned, `System.Threading.Tasks.*` banned, closed member allowlist, branch-free `Execute` shape required) and the plan pivoted to the trampoline design that leaves compiled IL and all public baselines untouched.
