---
id: API-0027
area: api_coherence
status: open
priority: medium
title: SandboxResourceUsage omits call-depth and wall-time usage
dedup_key: api/core/resource-usage/call-depth-wall-time-telemetry-missing
created_at: 2026-06-13T06:52:08.9635104+00:00
created_by: core-completeness-producer
created_commit: 
updated_at: 2026-06-13T06:52:08.9635104+00:00
claimed_by: 
claimed_at: 
claim_branch: 
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0027: SandboxResourceUsage omits call-depth and wall-time usage

## Claim

`DotBoxd.Kernels` exposes call-depth and wall-time limits as first-class public policy knobs, and the runtime enforces them, but the typed execution result does not report the corresponding usage or applied ceilings. `SandboxResourceUsage` reports fuel, loops, allocations, host calls, I/O, network, log, collection, and string counters, but it omits peak/current call depth and elapsed/effective wall time.

This leaves two public resource-limit dimensions without consistent public result-model coverage.

## Evidence

- `src/DotBoxd.Kernels/Model/ResourceLimits.cs` defines public `MaxWallTime` and `MaxCallDepth` alongside the other resource ceilings.
- `src/DotBoxd.Kernels/Policy.cs` exposes `SandboxPolicyBuilder.WithMaxCallDepth(...)` and `SandboxPolicyBuilder.WithWallTime(...)` as public builder methods.
- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs` enforces call-depth through `EnterCall()` / `ExitCall()` against `Budget.Limits.MaxCallDepth`.
- `src/DotBoxd.Kernels/Model/Resources.cs` enforces wall time through a private deadline, `CheckDeadline()`, and `RemainingWallTime()`.
- `src/DotBoxd.Kernels/Sandbox/SandboxResourceUsage.cs` contains `FuelUsed`, `MaxFuel`, `LoopIterations`, `AllocatedBytes`, `HostCalls`, file/network byte counters, `LogEvents`, `CollectionElements`, and `StringBytes`, but no call-depth or wall-time fields.
- `src/DotBoxd.Kernels/Model/Resources.cs` `Snapshot()` constructs `SandboxResourceUsage` without any call-depth or wall-time data.
- `src/DotBoxd.Kernels/Model/RunSummaryAuditFields.cs` emits usage and ceiling fields for many resources, but it also omits call-depth and wall-time usage/ceiling fields, so callers cannot recover this data from the structured audit summary either.

## Impact

Hosts can configure call-depth and wall-time budgets, but after execution they cannot inspect how much of either budget was used through the stable typed result. Operators cannot build dashboards, per-run diagnostics, or policy tuning around maximum observed call depth or elapsed/effective wall time the way they can for fuel, loops, host calls, log events, strings, and collections. Worker-process envelopes also have no typed field to carry these dimensions, reinforcing that the public result model is incomplete for two advertised limits.

## Suggested fix direction

Extend the public result model so every public resource-limit family has corresponding typed telemetry. For example:

- Track peak call depth in `SandboxContext` or `ResourceMeter` and expose it as `SandboxResourceUsage.CallDepth` or `PeakCallDepth`, plus the applied maximum if ceilings remain on the usage model.
- Track elapsed wall time or deadline-relative duration in the execution runners and expose `ElapsedWallTime` / `MaxWallTime` / `EffectiveWallTime` through a backward-compatible result telemetry shape.
- Include the same fields in `RunSummaryAuditFields` and worker-result validation so in-process and worker-isolated execution publish the same resource model.

Because `SandboxResourceUsage` is a positional record, choose an API-compatible shape deliberately, such as additive init properties or a versioned nested telemetry record, rather than silently breaking consumers.

## Non-duplicates checked

`CMP-0019` covers missing runnable documentation/smoke proof for non-fuel limits. `COR-0054` covers worker acceptance of forged run-summary budget ceilings for fields that are already emitted. This finding is distinct: call-depth and wall-time have no typed usage/result fields at all despite being public enforced limits.

## Deduplication key

`api/core/resource-usage/call-depth-wall-time-telemetry-missing`
