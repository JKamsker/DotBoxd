---
id: PAL-0050
area: perf_alloc
status: open
priority: medium
title: Async live-state updates allocate task continuations per input
dedup_key: alloc/plugins/live-state-async/task-continuation-per-update
created_at: 2026-06-13T06:50:37.0848492+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:50:37.0848492+00:00
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

# PAL-0050: Async live-state updates allocate task continuations per input

## Claim

`LiveUpdateMode.AsyncSet` live-state synchronization allocates and schedules independent background tasks for every deferred update produced while building plugin inputs. Each async-set event update goes through `Task.Run(...)`, stores the task in a list, and adds a continuation that later removes the completed task from that list.

## Evidence

- `src/DotBoxd.Plugins/InstalledKernel.cs` builds every plugin event input through `PluginKernelInputBuilder.Build(...)`, passing `_liveStateSync.SynchronizeForInput()` and `_pendingLiveUpdates.Enqueue`.
- `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs` iterates all deferred updates after input construction and calls the enqueue delegate for each one.
- `src/DotBoxd.Plugins/Runtime/Lifecycle/PendingLiveUpdateQueue.cs` implements `Enqueue(...)` by starting a `Task.Run(...)` for each update and registering a separate `ContinueWith(...)` callback to remove the completed task from `_pending`.
- The same queue stores pending work in a `List<Task>`; each completion removes its task from that list, so bursts of async updates also pay linear list search/removal under the queue lock.
- Existing `PAL-0047` covers the synchronous hot path that allocates an empty deferred-update list even when no async updates exist. This finding is distinct: when `AsyncSet` is actually enabled, the enqueue path allocates/schedules task machinery per input update and performs linear pending-list maintenance.

## Impact

`AsyncSet` is intended to decouple direct kernel state updates from the next plugin run, but high-frequency event streams can produce one background task and one continuation per live-state synchronizer per event before sandbox execution proceeds. Under bursts, the queue also serializes completion cleanup through a locked list and `List.Remove`, adding O(pending-count) cleanup work per completed update. That creates avoidable Gen0 pressure and scheduler overhead on plugin event dispatch paths that are otherwise expected to stay lightweight.

## Better target

Use a bounded/coalescing async update worker per installed kernel instead of spawning a task per deferred update. A channel or single background drain can preserve ordering, record the last error, and let multiple `AsyncSet` updates collapse to the latest state where that is semantically valid. If per-update tasks remain necessary, use a data structure with O(1) removal or avoid tracking completed tasks individually on the dispatch path.

## Benchmark/allocation test idea

Add a plugin dispatch allocation benchmark with a class-shaped live state in `LiveUpdateMode.AsyncSet`, publishing 1,000, 10,000, and 100,000 events through `ShouldHandleAsync` or hook `InvokeAsync`. Measure allocations and scheduled tasks before sandbox execution, and assert async live-state synchronization does not create one `Task.Run` plus continuation per deferred update.
