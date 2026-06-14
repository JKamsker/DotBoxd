---
id: PAL-0020
area: perf_alloc
status: open
priority: medium
title: Policy hash recomputes canonical grant records on every access
dedup_key: alloc/policy-hash/recompute-per-access
created_at: 2026-06-12T22:15:10.1381187+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:15:10.1381187+00:00
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

# PAL-0020: Policy hash recomputes canonical grant records on every access

## Claim

`SandboxPolicy.Hash` recomputes the full canonical policy hash on every property access, and the prepare/execute plan paths read it multiple times per plan build.

## Evidence

- `src/DotBoxd.Kernels/Policy.cs:28` exposes `public string Hash => StableHash();`, so the value is not cached on the immutable policy record.
- `src/DotBoxd.Kernels/Policy.cs:43` routes every access to `PolicyHash.Compute(this)`.
- `PolicyHash.Compute` allocates canonical records with `new List<string>` at `src/DotBoxd.Kernels/Model/PolicyHash.cs:7`, and grant hashing allocates `string[]` / `List<string?>` records at `src/DotBoxd.Kernels/Model/PolicyHash.cs:27` and `src/DotBoxd.Kernels/Model/PolicyHash.cs:66`.
- `CanonicalEncoding.HashRecords` joins all policy records into one string at `src/DotBoxd.Kernels/CanonicalEncoding.cs:23`, and `HashText` encodes that string into a UTF-8 byte array at `src/DotBoxd.Kernels/CanonicalEncoding.cs:31`.
- `ExecutionPlanBuilder.Build` reads `policy.Hash` at `src/DotBoxd.Hosting/Execution/ExecutionPlanBuilder.cs:18`, `src/DotBoxd.Hosting/Execution/ExecutionPlanBuilder.cs:24`, and `src/DotBoxd.Hosting/Execution/ExecutionPlanBuilder.cs:50` for a single plan build.
- `SandboxHost.PrepareAsync` calls `ExecutionPlanBuilder.Build` at `src/DotBoxd.Hosting/Execution/SandboxHost.cs:59`, and `ExecutionPlanGuard.EnsurePrepared` rebuilds an expected plan at `src/DotBoxd.Hosting/Execution/ExecutionPlanGuard.cs:39` before every execution.

## Impact

Policy hashes are stable for a `SandboxPolicy` instance, but current access recomputes and reallocates the canonical policy representation repeatedly. A policy with many grants or grant parameters pays that cost at least three times per plan build, then again when executing because the integrity guard rebuilds the plan. This adds avoidable CPU and allocation to prepare/execute paths, especially for plugin servers with fixed default policies reused across many installs/runs.

## Better target

Cache the computed hash in `SandboxPolicy` during construction or via a thread-safe lazy field, preserving immutability and deterministic behavior. Plan building should compute/read the policy hash once and pass the local value through `planHash`, `Seal`, and `ExecutionPlan` construction.

## Benchmark/allocation test idea

Add a benchmark that creates policies with 0, 10, 100, and 1,000 grants/parameters, then measures repeated `policy.Hash`, `SandboxHost.PrepareAsync`, and `SandboxHost.ExecuteAsync` plan-integrity validation. Assert a reused policy does not allocate proportional to grant count on every hash read.
