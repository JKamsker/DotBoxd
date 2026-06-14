---
id: ALG-0015
area: perf_algorithm
status: open
priority: medium
title: Policy validation rescans grants for every required capability
dedup_key: algorithm/policy-validation/grant-lookup/repeated-linear-scans-per-required-capability
created_at: 2026-06-12T23:12:24.4439606+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T23:12:24.4439606+00:00
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

# ALG-0015: Policy validation rescans grants for every required capability

## Claim

Policy validation walks active grants once, then separately rescans the same grant list for every module-requested and inferred required capability. A single prepare or integrity-validation pass therefore pays O(grant-count * capability-count) membership checks after `PolicyGrantValidator` already paid O(grant-count) to validate the grants.

## Evidence

- `src/DotBoxd.Kernels.Validation/PolicyGrantValidator.cs:19` captures the policy grant clock, `src/DotBoxd.Kernels.Validation/PolicyGrantValidator.cs:20` checks duplicate active grants, and `src/DotBoxd.Kernels.Validation/PolicyGrantValidator.cs:21` through `src/DotBoxd.Kernels.Validation/PolicyGrantValidator.cs:26` walks `policy.Grants` to validate each active grant.
- `src/DotBoxd.Kernels.Validation/Internal/PolicyResolver.cs:23` calls `PolicyGrantValidator.Validate(...)` before doing capability-presence checks, so the active grant list has already been traversed for the current validation pass.
- `src/DotBoxd.Kernels.Validation/Internal/PolicyResolver.cs:25` through `src/DotBoxd.Kernels.Validation/Internal/PolicyResolver.cs:29` then loops over `module.CapabilityRequests` and calls `policy.GrantsCapability(request.Id)` for each request.
- `src/DotBoxd.Kernels.Validation/Internal/PolicyResolver.cs:31` through `src/DotBoxd.Kernels.Validation/Internal/PolicyResolver.cs:35` repeats the same pattern for every inferred required capability in `requiredCapabilities`.
- `src/DotBoxd.Kernels/Policy.cs:33` through `src/DotBoxd.Kernels/Policy.cs:36` implements `GrantsCapability` with `Grants.Any(...)`, so each request/capability check starts another linear scan of the policy grant list.
- `src/DotBoxd.Kernels/Policy.cs:30` exposes `GrantClock` as `DateTimeOffset.UtcNow` for nondeterministic policies, which is evaluated by every `GrantsCapability` call rather than once for the validation pass.
- `src/DotBoxd.Hosting/Execution/SandboxHost.cs:52` validates policy and module during `PrepareAsync`, making this a production setup path. `ALG-0013` covers that `ExecutionPlanGuard` repeats the whole validation per execution; this finding is the avoidable repeated grant lookup inside one validation pass. `ALG-0009` covers per-binding-call runtime authorization, not prepare-time policy validation.

## Impact

Hosts that prepare generated modules or plugin packages with many declared/requested capabilities and many policy grants pay validation work proportional to grants times capabilities. For example, 100 active grants and 100 required/requested capabilities cause roughly 10,000 active-grant predicate checks after grant validation has already scanned the grant list. Because prepare and integrity checks sit before execution dispatch, this fixed cost can dominate small sandbox runs and plugin install/hot-reload paths.

## Better target

Build an active capability index once per validation pass, or expose an immutable indexed lookup from `SandboxPolicy` that captures the grant clock once. Reuse that index for duplicate detection, unsupported-grant validation, module request checks, and inferred required-capability checks. Keep the diagnostic behavior the same, but make capability membership O(1) per capability after the active-grant pass.

## Benchmark idea

Add a benchmark that constructs policies with 10, 100, and 1,000 grants and modules with 10, 100, and 1,000 requested/inferred capabilities. Measure `ModuleValidator.Validate`, `SandboxHost.PrepareAsync`, and `ExecutionPlanGuard.EnsurePrepared` before and after indexing active grants, and assert validation no longer scales with grant-count times capability-count.
