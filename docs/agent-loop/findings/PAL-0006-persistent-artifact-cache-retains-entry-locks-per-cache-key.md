---
id: PAL-0006
area: perf_alloc
status: verified
priority: medium
title: Persistent artifact cache retains entry locks per cache key
dedup_key: alloc/compiler-cache/entry-locks/unbounded-key-retention
created_at: 2026-06-12T21:03:29.0265407+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T21:55:54.9137935+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:50:28.2244969+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:53:55.6814818+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T21:55:54.9137935+00:00
verified_commit: 
duplicate_of: 
---

# PAL-0006: Persistent artifact cache retains entry locks per cache key

## Claim

`PersistentCompiledArtifactCache` retains one in-memory `SemaphoreSlim` per cache key forever, so long-lived hosts that compile many unique artifacts accumulate lock objects even after entries are no longer active.

## Evidence

- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:18` stores entry locks in a `ConcurrentDictionary<string, SemaphoreSlim>` keyed by cache key.
- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:170` uses `_entryLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1))` for every read.
- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:191` repeats the same `GetOrAdd` path for every write.
- Neither `WithEntryLockAsync` overload removes the lock from `_entryLocks` after the protected operation completes, and the cache type does not expose disposal/cleanup for accumulated semaphores.
- Cache keys are content-derived, so a service compiling many module/policy/entrypoint combinations can create unbounded distinct keys over time.
- Existing compiled cache tests cover behavior and concurrency, but there is no memory-retention test that compiles many unique cache keys and asserts entry-lock cleanup.

## Impact

This is a cache-behavior memory growth issue rather than a per-call micro-allocation. In a daemon or game server that continuously receives new generated modules, `_entryLocks` grows with historical cache keys and retains `SemaphoreSlim` instances after the corresponding read/write has finished. Disk cache eviction would not reclaim these in-memory locks.

## Better target

Use a ref-counted/removable keyed lock, remove the semaphore when no waiters remain, or bound the in-memory lock table with eviction. Any fix must preserve mutual exclusion across concurrent read/write attempts for the same cache key.

## Benchmark/allocation test idea

Add a stress/allocation test that invokes cache read/write paths for 10,000 unique synthetic cache keys, waits for all operations to complete, and verifies the in-memory lock table does not retain O(unique-key) entries. Pair it with a concurrency test for same-key serialization.
