---
id: PAL-0031
area: perf_alloc
status: open
priority: medium
title: Compiled executable cache never evicts materialized artifacts
dedup_key: alloc/compiled-executable-cache/unbounded-materialized-artifacts
created_at: 2026-06-12T22:30:59.6209854+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:30:59.6209854+00:00
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

# PAL-0031: Compiled executable cache never evicts materialized artifacts

## Claim

`CompiledExecutableCache` retains every materialized compiled executable until the host is disposed, with no capacity, age, or plan-retirement eviction.

## Evidence

- `src/DotBoxd.Hosting/Execution/CompiledExecutableCache.cs` stores materialized entries in a `ConcurrentDictionary<string, Lazy<Task<MaterializedCompiledArtifact>>>`.
- `GetAsync` keys entries by `artifact.Manifest.CacheKey + "|" + artifact.AssemblyHash`, so every unique compiled artifact identity creates a distinct cache slot.
- Successful materializations are reused from `_entries.GetOrAdd(...)`, but entries are removed only on cancellation/failure or when `Dispose()` clears the entire dictionary.
- `SandboxHost` owns one `CompiledExecutableCache` for its lifetime and only disposes it when the host itself is disposed.
- Existing compiled-cache findings cover persistent cache entry locks, lock files, quarantine cleanup, and cancellation poisoning; they do not cover unbounded retention of loaded/materialized executables in the host-local executable cache.

## Impact

A long-lived host that compiles many generated modules, policy variants, binding manifest variants, or plugin entrypoints retains loaded delegates/assemblies and their associated metadata for every historical artifact. This can grow process memory even when the persistent cache is pruned and no active run references older plans.

## Better target

Add bounded eviction to `CompiledExecutableCache`, such as size/age limits with safe disposal of evicted `MaterializedCompiledArtifact` instances, or associate entries with a prepared-plan lifetime so retired plans release materialized executables. Keep same-key coalescing, but avoid retaining all historical compiled artifacts indefinitely.

## Benchmark/allocation test idea

Add a stress test that materializes thousands of unique compiled artifacts through one `SandboxHost`, drops plan references, and asserts retained executable-cache entries and memory are bounded after eviction. Include a same-key concurrency case to preserve existing coalescing behavior.
