---
id: CMP-0019
area: completeness
status: open
priority: medium
title: Non-fuel resource limits lack a runnable consumer-facing proof
dedup_key: completeness/resource-limits/non-fuel-runnable-proof
created_at: 2026-06-12T23:30:03.0727482+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T23:30:03.0727482+00:00
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

# CMP-0019: Non-fuel resource limits lack a runnable consumer-facing proof

## Claim

DotBoxd.Kernels exposes a broad public resource-limit surface beyond fuel, but the consumer-facing docs and runnable examples do not prove that surface end to end. Hosts can configure loop, host-call, call-depth, wall-time, allocation, collection, string, and log quotas, and `SandboxExecutionResult.ResourceUsage` reports those counters, but the visible examples only show fuel and one host-call default.

This leaves non-fuel quota behavior as spec/API text instead of a package-backed sample or release proof that users can run while integrating the public packages.

## Why this matters

Resource limits are part of DotBoxd.Kernels's core sandbox contract. If consumers only see `WithFuel(...)`, they do not get a clear pattern for configuring the other limits, recognizing the public `QuotaExceeded` or `Timeout` results, or reading the corresponding usage counters. A release can also regress non-fuel limit wiring while docs smoke still passes because no user-facing sample exercises those knobs.

## Evidence

- `src/DotBoxd.Kernels/Policy.cs:163` through `src/DotBoxd.Kernels/Policy.cs:233` expose non-fuel builder methods for loop iterations, host calls, call depth, wall time, allocations, collection shape, log quotas, and string quotas.
- `src/DotBoxd.Kernels/Model/ResourceLimits.cs:3` defines the public default limit record used by policies and plans.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:187` through `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:200` list the non-fuel limit methods, and `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:206` describes the resource usage counters.
- `README.md:39` only demonstrates `.WithFuel(10_000)` in the minimal host path.
- `docs/Specs/Addendum/Examples.md:265` and `docs/Specs/Addendum/Examples.md:266` show only fuel plus `WithMaxHostCalls` in the plugin policy example, not a general quota walkthrough.
- `docs/Specs/Initial/dotboxd-sandbox-spec/checklists/release-readiness.md:19` marks fuel limits as implemented in the required gate, while worker resource limits remain inventory-only at `docs/Specs/Initial/dotboxd-sandbox-spec/checklists/release-readiness.md:48`.

## Suggested test or benchmark

Add a docs-smoke or example project that executes small JSON IR modules under intentionally tight non-fuel limits. It should cover at least loop iteration exhaustion, host-call exhaustion, wall-time timeout, collection or string shape rejection, and log-event/message limits, then assert the public result code and `SandboxResourceUsage` fields.

## Suggested fix direction

Add a short "resource limits" public walkthrough and wire it into `scripts/check-docs-smoke.ps1` or an equivalent release validation step. Keep each fixture small and deterministic so it proves user-facing configuration and result handling without becoming a full correctness test matrix.

## Scope boundaries

Do not change quota semantics as part of this docs/proof work unless the smoke sample exposes an existing behavior bug. Correctness fixes should be filed separately if needed.

## Deduplication key

`completeness/resource-limits/non-fuel-runnable-proof`

## Verification checklist

- [ ] Reproduction or test exists where practical.
- [ ] Fix addresses root cause.
- [ ] Relevant tests pass.
- [ ] Perf/allocation evidence exists where practical.
- [ ] No unrelated behavior changed.
