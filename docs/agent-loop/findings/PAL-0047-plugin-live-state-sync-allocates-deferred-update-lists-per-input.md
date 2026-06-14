---
id: PAL-0047
area: perf_alloc
status: open
priority: medium
title: Plugin live-state sync allocates deferred update lists per input
dedup_key: alloc/plugins/live-state-sync/deferred-updates-list-per-input
created_at: 2026-06-13T06:45:32.6644581+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:45:32.6644581+00:00
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

# PAL-0047: Plugin live-state sync allocates deferred update lists per input

Class-shaped plugin live-state synchronization allocates a deferred-update list for every input build, even when the common synchronous update mode has no deferred work to enqueue.

## Evidence

- `src/DotBoxd.Plugins/InstalledKernel.cs:289` passes `_liveStateSync.SynchronizeForInput()` into `PluginKernelInputBuilder.Build(...)` for every plugin event input.
- `src/DotBoxd.Plugins/Runtime/Lifecycle/LiveStateSyncRegistry.cs:10` implements the per-input synchronization method, and `src/DotBoxd.Plugins/Runtime/Lifecycle/LiveStateSyncRegistry.cs:12` unconditionally allocates `new List<Action>()` before inspecting synchronizer modes.
- `src/DotBoxd.Plugins/Runtime/Lifecycle/LiveStateSyncRegistry.cs:18` only adds to that list for `LiveUpdateMode.AsyncSet`; the default mode returned by `InstalledKernel.GetUpdateMode(...)` is synchronous, so the hot path commonly returns an empty heap-allocated list.
- `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:19` then enumerates the returned list only to enqueue deferred updates, so an empty result has no semantic work.
- Existing `PAL-0032` covers reflection/property synchronization cost, `PAL-0040` covers double-copying multi-value plugin inputs, and `PAL-0036` covers linked cancellation tokens. This finding is separate: per-input live-state sync allocates an empty deferred-update container before input construction even when no async update exists.

## Impact

High-frequency plugin hosts call `ShouldHandleAsync`, `HandleAsync`, or hook `InvokeAsync` for each event. With class-shaped live state in the default synchronous mode, each input build pays a short-lived `List<Action>` allocation that is unrelated to event values, sandbox execution, or actual deferred updates. The cost compounds with multiple installed kernels and remains after optimizing event value copying or class property reflection.

## Suggested fix direction

Return a shared empty list/array when no synchronizer is async, or split synchronization into a `bool TrySynchronizeForInput(out IReadOnlyList<Action> deferredUpdates)`/callback shape that only allocates a deferred collection after the first `AsyncSet` synchronizer is encountered. Keep synchronous synchronizer execution inline and preserve async enqueue ordering.

## Benchmark/allocation test idea

Add a plugin dispatch allocation benchmark with a class-shaped live state and default `LiveUpdateMode.Sync`, publishing 1,000, 10,000, and 100,000 events through `ShouldHandleAsync`/`HandleAsync`. Measure bytes allocated before sandbox execution and assert synchronous live-state sync returns an allocation-free empty deferred-update result. Include an `AsyncSet` case to verify the deferred list still allocates only when needed.
