---
id: ALG-0013
area: perf_algorithm
status: open
priority: medium
title: Execution plan integrity guard revalidates and reseals modules on every run
dedup_key: algorithm/execution-plan/integrity-guard/full-revalidation-per-run
created_at: 2026-06-12T22:30:57.0898332+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:30:57.0898332+00:00
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

# ALG-0013: Execution plan integrity guard revalidates and reseals modules on every run

## Claim

`SandboxHost.ExecuteAsync` revalidates the whole prepared module and rebuilds the expected execution plan before every run, so execution cost includes full validation, binding-reference analysis, canonical hashing, and plan sealing even when the caller reuses an unchanged `ExecutionPlan`.

## Evidence

- `src/DotBoxd.Hosting/Execution/SandboxHost.cs` calls `ExecutionPlanGuard.EnsurePrepared(plan, _bindings, _planSigningKey)` at the start of every `ExecuteAsync` call before mode selection or dispatch.
- `src/DotBoxd.Hosting/Execution/ExecutionPlanGuard.cs:32` constructs a fresh `ModuleValidator` and validates `plan.Module` against the host bindings and policy on each execution.
- When validation succeeds, `src/DotBoxd.Hosting/Execution/ExecutionPlanGuard.cs:39` calls `ExecutionPlanBuilder.Build(...)` again to produce an expected plan for comparison.
- `src/DotBoxd.Hosting/Execution/ExecutionPlanBuilder.cs:17` hashes the whole module during each build, and `Seal(...)` materializes ordered function-analysis and binding-reference records before HMAC sealing.
- Existing PAL-0019 and PAL-0020 cover specific hashing allocation inside this path; this finding is the broader execution algorithm: the guard repeats the entire prepare-time validation/seal pipeline for every run of an already prepared plan.

## Impact

A host that prepares once and executes a plan many times still pays prepare-scale work on every execution. For large generated modules or plugin modules with many functions, entrypoints, and binding references, the per-run fixed cost can dominate small sandbox invocations and grows with module size instead of with the selected entrypoint's execution work.

## Better target

Validate and seal once during `PrepareAsync`, then make execution integrity checks O(1) against immutable prepared-plan identity: host binding identity, plan seal/signature, policy hash, module hash, and binding manifest hash. If defensive revalidation remains necessary, make it opt-in diagnostics or cache the validated expected identity by plan seal rather than rebuilding it on every dispatch.

## Benchmark/allocation test idea

Add a benchmark that prepares modules with 10, 100, and 1,000 functions, then executes a trivial entrypoint 1, 100, and 10,000 times. Measure time and allocations in `SandboxHost.ExecuteAsync` before user code dispatch, and assert repeated execution of the same prepared plan does not scale with total module size.
