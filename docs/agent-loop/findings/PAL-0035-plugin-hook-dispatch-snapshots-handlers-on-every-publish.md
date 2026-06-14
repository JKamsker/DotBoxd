---
id: PAL-0035
area: perf_alloc
status: open
priority: medium
title: Plugin hook dispatch snapshots handlers on every publish
dedup_key: alloc/plugins/hook-dispatch/per-publish-handler-snapshots
created_at: 2026-06-12T23:03:01.6914378+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T23:03:01.6914378+00:00
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

# PAL-0035: Plugin hook dispatch snapshots handlers on every publish

## Claim

Plugin hook dispatch snapshots filter and handler lists into new arrays on every event publish. Pipelines are usually stable after setup, but `HookPipeline<TEvent>.PublishAsync` still allocates two arrays per published event before invoking filters and handlers.

## Evidence

- `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:54` through `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:65` is the public event dispatch path for `PluginServer.Hooks.PublishAsync`.
- `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:71` through `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:73` stores filters and handlers in mutable `List<...>` fields.
- `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:91` through `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:106` mutates those lists under the pipeline lock during setup, and `UseKernel` adds plugin handlers through the same path at `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:126` through `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:129`.
- `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:138` through `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:146` locks on every publish and calls `_filters.ToArray()` plus `_handlers.ToArray()`, allocating snapshots proportional to pipeline size even when no lifecycle mutation is happening.
- `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:149` through `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:159` then iterates those snapshots for every event.
- Existing `COR-0012` was a correctness finding about concurrent mutation and stable publish snapshots; it is now verified. This finding is separate: the current stable-snapshot implementation pays per-publish allocation on the hot event path. Existing `PAL-0004` covers convention event adapter reflection/arrays and `PAL-0033` covers plugin execution observation retention, not hook handler-list snapshots.

## Impact

Game/UI/server hosts can publish hooks at high frequency while the filter/handler pipeline changes rarely. The current implementation allocates at least two arrays per event publish, and the arrays grow with each filter and installed kernel/host handler. That allocation happens before sandbox execution and is paid even for events rejected by the first filter.

## Better target

Use copy-on-write immutable arrays for filters and handlers: mutate under the existing lock by replacing cached arrays, and let publish read the current arrays without allocating or holding the mutation lock while invoking delegates. Preserve stable per-publish semantics by reading each array reference once at the start of `PublishAsync`.

## Benchmark/allocation test idea

Add a plugin hook dispatch benchmark with 0, 1, 5, and 20 filters/handlers and 1,000 to 100,000 published events. Measure allocated bytes before sandbox execution and assert steady-state publish does not allocate handler/filter snapshot arrays.
