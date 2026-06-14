---
id: PAL-0015
area: perf_alloc
status: open
priority: low
title: Compiled cache quarantine has no bounded cleanup policy
dedup_key: alloc/compiler-cache/quarantine/unbounded-invalid-entry-retention
created_at: 2026-06-12T22:07:34.1117422+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:07:34.1117422+00:00
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

# PAL-0015: Compiled cache quarantine has no bounded cleanup policy

## Claim

Persistent compiled cache cleanup quarantines invalid cache entries into a `quarantine` directory but never prunes those quarantined artifacts, so invalid/stale cache probes can grow disk usage and directory traversal cost without bound.

## Evidence

- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:128` catches invalid cached entries and calls `Quarantine(entryPath)` before returning `CompiledCacheStatus.Invalid`.
- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:217` creates a `quarantine` directory under the cache root.
- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:221` builds a unique quarantine target from the cache key, timestamp, and GUID, and `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:226` moves the invalid entry there.
- `src/DotBoxd.Kernels.Compiler/Internal/PersistentCompiledArtifactCachePublisher.cs:28` deletes temp/old paths during publish cleanup, but there is no corresponding retention policy or cleanup path for the `quarantine` tree.
- Existing compiled cache tests exercise quarantining and recompilation (`tests/DotBoxd.Kernels.Tests/Compiled/Core/CompiledCacheTests.cs`), while PAL-0010 separately covers `.locks` files; neither covers bounded quarantine cleanup.

## Impact

A long-lived host or CI cache that sees repeated corrupted, stale, or policy-mismatched compiled artifacts will keep every quarantined `module.dll`, `manifest.json`, and `verification.json`. This is a cache cleanup performance issue distinct from lock-file accumulation: the retained payloads can be much larger and can slow cache backup, cleanup, and filesystem scans.

## Better target

Add a bounded quarantine retention policy, such as max age, max bytes, or max entries, and run it safely during cache initialization or after quarantining. Cleanup must preserve current diagnostics long enough for troubleshooting while preventing unbounded growth.

## Benchmark/allocation test idea

Add a cache cleanup stress test that seeds 1,000 invalid cache entries, triggers cache reads that quarantine them, and then invokes the cleanup policy. Assert the quarantine directory is bounded by configured count/bytes and measure cleanup time so pruning is not O(total historical entries) on every cache access.
