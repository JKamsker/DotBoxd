---
id: PAL-0010
area: perf_alloc
status: open
priority: low
title: Persistent compiled cache leaves lock files per cache key
dedup_key: alloc/compiler-cache/file-locks/persistent-key-files
created_at: 2026-06-12T22:02:49.6109213+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:02:49.6109213+00:00
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

# PAL-0010: Persistent compiled cache leaves lock files per cache key

## Claim

The persistent compiled artifact cache now removes in-memory entry locks, but cross-process file locks are created under `.locks` with `OpenOrCreate` and are never deleted, so long-lived cache directories accumulate one lock file per historical cache key.

## Evidence

- `src/SafeIR.Compiler/Internal/PersistentCacheEntryLock.cs` computes lock paths under `.locks/<prefix>/<prefix>/<cacheKey>.lock`.
- `AcquireAsync` opens each lock path with `FileMode.OpenOrCreate`, creating a persistent filesystem entry for every unique cache key that reaches a cache read/write lock.
- `DisposeAsync` only disposes the `FileStream`; it does not remove the lock file after the lock is released.
- `src/SafeIR.Compiler/Internal/PersistentCompiledArtifactCachePublisher.cs` deletes temp/old/final cache entries, but cache entry cleanup does not include the `.locks` tree.
- This is distinct from `PAL-0006`: the in-memory `ConcurrentDictionary<string, EntryLock>` now releases entries, but the filesystem lock artifacts remain unbounded.

## Impact

A service compiling or probing many unique plans/policies/entrypoints can leave thousands of stale `.lock` files even after artifacts are invalidated, quarantined, overwritten, or manually pruned. That increases disk metadata footprint and makes cache cleanup/backups/directory traversal slower over time.

## Better target

Delete lock files after releasing them when no process still needs the key, or provide bounded lock-file cleanup tied to cache pruning. If immediate deletion is racy across processes, add a safe stale-lock scavenger that removes unlocked files older than a grace period while preserving same-key mutual exclusion.

## Benchmark/allocation test idea

Add a cache cleanup stress test that performs read/write attempts for 10,000 unique synthetic cache keys, disposes all operations, prunes cache entries, and asserts the `.locks` tree does not retain O(unique-key) files. Pair it with a concurrency test showing same-key cross-process/file-lock serialization still holds.
