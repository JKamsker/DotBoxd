---
id: ALG-0009
area: perf_algorithm
status: open
priority: medium
title: Capability checks rescan grants per binding call
dedup_key: algorithm/policy/capability-lookup/repeated-linear-scans-per-binding
created_at: 2026-06-12T22:20:11.8606582+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:20:11.8606582+00:00
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

# ALG-0009: Capability checks rescan grants per binding call

## Claim

Capability checks rescan the policy grant list multiple times for a single binding call, so hot binding-heavy workloads pay repeated O(grant-count) scans instead of one indexed capability lookup.

## Evidence

- `src/DotBoxd.Kernels/Policy.cs:26` stores policy grants as an array-backed `IReadOnlyList<CapabilityGrant>`.
- `src/DotBoxd.Kernels/Policy.cs:33` through `src/DotBoxd.Kernels/Policy.cs:36` implements `GrantsCapability` with `Grants.Any(...)`, scanning the grant list until it finds an active matching capability.
- `src/DotBoxd.Kernels/Policy.cs:38` through `src/DotBoxd.Kernels/Policy.cs:44` implements `GetGrant` with `Grants.FirstOrDefault(...)`, scanning the same list again to return the matching grant.
- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:41` through `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:57` routes `RequireCapability` through `Policy.GrantsCapability`.
- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:59` through `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:63` makes `GetCapability` call `RequireCapability` first, then `Policy.GetGrant`, which means `GetCapability` performs two list scans on success.
- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:202` through `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:213` charges a binding call and invokes `RequireCapability` for descriptors with a required capability.
- Both interpreted and compiled binding dispatch go through `ChargeBindingCall`: `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:173` and `src/DotBoxd.Kernels.Runtime/CompiledBindingDispatcher.cs:14`.
- File and HTTP bindings then repeat capability resolution inside the binding implementation: `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:138` and `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:139` call `RequireCapability` and `GetCapability`, while `src/DotBoxd.Hosting.Http/SafeHttpClient.cs:93` and `src/DotBoxd.Hosting.Http/SafeHttpClient.cs:94` do the same for `net.http.get`.
- This is distinct from `PAL-0020`, which covers recomputing policy hash records, and from `PAL-0022`, which covers parsing file grant parameters after a grant has been found. The issue here is repeated linear lookup of stable grants.

## Impact

A single successful file or HTTP binding call can scan the grants in `ChargeBindingCall`, scan again in the binding-specific `RequireCapability`, then scan twice more through `GetCapability`. With many granted capabilities or short-lived binding calls, policy lookup cost grows with grant count and call count even though the policy is immutable for the run. Expiring grants also evaluate active-grant time checks during those scans.

## Better target

Build an immutable capability index when constructing or validating `SandboxPolicy`, keyed by capability ID and containing the currently relevant grants or typed grant state. `GetCapability` should perform one lookup and return the grant or denial result; binding implementations should reuse the grant already authorized by the dispatch layer where possible instead of rechecking the same capability.

## Benchmark idea

Add a benchmark that creates policies with 1, 10, 100, and 1,000 grants, then runs 10,000 pure, file, and HTTP-style binding calls that require a capability near the end of the grant list. Measure time and allocations for interpreted and compiled dispatch before and after introducing an indexed grant lookup and avoiding duplicate per-call checks.
