---
id: PAL-0030
area: perf_alloc
status: open
priority: medium
title: Auto execution hotness retains states for every plan entrypoint
dedup_key: alloc/auto-hotness/unbounded-plan-entrypoint-state
created_at: 2026-06-12T22:30:58.3615077+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:30:58.3615077+00:00
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

# PAL-0030: Auto execution hotness retains states for every plan entrypoint

## Claim

Auto execution mode retains one `AutoHotnessState` per `planHash|entrypoint` for the lifetime of the host, with no eviction or reset path.

## Evidence

- `src/DotBoxd.Hosting/Internal/AutoExecutionHotness.cs:8` stores states in a `ConcurrentDictionary<string, AutoHotnessState>`.
- `src/DotBoxd.Hosting/Internal/AutoExecutionHotness.cs:12` uses `_states.GetOrAdd(Key(plan.PlanHash, entrypoint), ...)` for every auto-mode attempt.
- `src/DotBoxd.Hosting/Internal/AutoExecutionHotness.cs:17` builds the key from `planHash + "|" + entrypoint`, so every distinct prepared plan and entrypoint creates a separate retained state.
- `src/DotBoxd.Hosting/Execution/SandboxHost.Auto.cs:21` calls `_autoHotness.BeginAttempt(plan, entrypoint)` on the auto execution path.
- The type exposes no removal, maximum size, age cutoff, or host-dispose cleanup for `_states`.
- This is distinct from persistent compiled-cache lock/quarantine findings: the retained data is host-local auto-mode scheduling/index state, not disk cache content or cache entry locks.

## Impact

Long-lived plugin servers or game hosts that continuously prepare generated modules, policy variants, or per-plugin entrypoints in `ExecutionMode.Auto` accumulate hotness records for historical plan hashes. Even after the plan is no longer used and compiled cache artifacts are evicted externally, the auto-mode index remains proportional to all distinct plan-entrypoint combinations seen by the host.

## Better target

Bound the hotness table with an LRU/TTL policy, expose a host-level reset for retired plans, or tie hotness lifetime to a bounded prepared-plan cache. The selector only needs recent execution history, so old plan hashes should be removable without changing sandbox semantics.

## Benchmark/allocation test idea

Add a stress/allocation test that executes auto mode for 10,000 unique plan hashes and entrypoints, then retires the plans. Assert retained hotness state is bounded after eviction/reset and measure dictionary size/allocation before and after cleanup.
