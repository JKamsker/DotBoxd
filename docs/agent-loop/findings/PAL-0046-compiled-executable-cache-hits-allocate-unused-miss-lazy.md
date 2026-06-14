---
id: PAL-0046
area: perf_alloc
status: open
priority: medium
title: Compiled executable cache hits allocate unused miss Lazy
dedup_key: alloc/compiled-executable-cache/cache-hit/unused-lazy-candidate
created_at: 2026-06-13T06:41:14.9166524+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:41:14.9166524+00:00
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

# PAL-0046: Compiled executable cache hits allocate unused miss Lazy

## Claim

Warm `CompiledExecutableCache` hits still allocate a miss candidate on every lookup. `GetAsync` constructs a new `Lazy<Task<MaterializedCompiledArtifact>>` and captures the artifact/plan/entrypoint in its factory before checking whether `_entries` already contains a materialized executable for the key.

## Evidence

- `src/DotBoxd.Hosting/Execution/CompiledExecutableCache.cs:33` through `src/DotBoxd.Hosting/Execution/CompiledExecutableCache.cs:38` validate the artifact and then allocate a fresh `Lazy<Task<MaterializedCompiledArtifact>>` candidate with a materialization lambda for every call.
- `src/DotBoxd.Hosting/Execution/CompiledExecutableCache.cs:43` calls `_entries.GetOrAdd(key, candidate)`. On a cache hit, the existing lazy is returned and the newly allocated candidate is discarded.
- `src/DotBoxd.Hosting/Execution/CompiledExecutableCache.cs:46` classifies hit/miss by comparing the returned lazy with that candidate, confirming the candidate is created even when it is only needed for the miss case.
- `src/DotBoxd.Hosting/Execution/SandboxHost.cs:19` stores a host-lifetime `CompiledExecutableCache`, and `src/DotBoxd.Hosting/Execution/SandboxHost.cs:234` through `src/DotBoxd.Hosting/Execution/SandboxHost.cs:237` calls `GetAsync` for compiled execution before running the cached delegate.
- Existing `ALG-0017` covers full artifact hashing and cache-key reconstruction before host-local materialized-cache hits. Existing `PAL-0031` covers unbounded retention of materialized executables. This finding is separate: even after hit validation is made cheap and retention is bounded, the hit path still allocates an unused miss `Lazy` and closure per lookup.

## Impact

Compiled mode is expected to amortize materialization once a plan/entrypoint is warm. For high-frequency compiled kernels or services repeatedly executing the same prepared plan, each cache hit still contributes avoidable Gen0 allocation before sandbox code starts. This fixed per-dispatch allocation is most visible after the larger `ALG-0017` artifact-size work is addressed, because the remaining cache-hit path should be a cheap dictionary lookup plus delegate invocation.

## Better target

Avoid constructing miss-only state until a miss is confirmed. Use a hit-first `TryGetValue` under the existing lock, or use a `GetOrAdd` overload/value factory shape that does not allocate a per-call `Lazy` and closure for hits. Keep same-key materialization coalescing and failure removal semantics, but make warm cache hits allocate no miss candidate.

## Benchmark/allocation test idea

Add a compiled executable cache benchmark that preloads one materialized artifact, then performs 1, 1,000, and 100,000 `GetAsync` calls for the same cache key. Measure allocated bytes and assert the warm hit path does not allocate an unused `Lazy<Task<MaterializedCompiledArtifact>>` or materialization closure per lookup.
