---
id: PAL-0032
area: perf_alloc
status: open
priority: medium
title: Class-shaped live state sync reflects over properties on modify and input
dedup_key: alloc/plugins/class-live-state/property-reflection-sync
created_at: 2026-06-12T22:47:21.5509898+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:47:21.5509898+00:00
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

# PAL-0032: Class-shaped live state sync reflects over properties on modify and input

## Claim

Class-shaped live kernel settings repeatedly rediscover live properties and use reflection get/set calls while synchronizing state around plugin execution and host-side modification.

## Evidence

- `src/DotBoxd.Plugins/Runtime/LiveKernelValueFactory.cs` handles non-interface settings by creating a class instance, discovering live properties with `type.GetProperties(...)`, then registering a state synchronizer that pushes the state back to the live setting store.
- The initial `Create<T>` path reuses the discovered property list for that one synchronizer, but the other class-shaped state paths call `LiveProperties(typeof(T))` or `LiveProperties(state.GetType())` again: `CreateDraft<T>`, `ExtractSettings<T>`, `CopyLiveProperties<T>`, and public `PullFromStore` all rediscover the same `PropertyInfo[]` shape.
- Those paths then call `PropertyInfo.GetValue` and `PropertyInfo.SetValue` for every live property during host modifications and refreshes.
- `src/DotBoxd.Plugins/InstalledKernel.cs` calls the class-shaped paths from `ModifyAsync<TState>` and `RefreshTypedValuesFromStore`, so a host that modifies class live state pays repeated property discovery plus reflection accessors.
- `src/DotBoxd.Plugins/Runtime/Lifecycle/LiveStateSyncRegistry.cs` invokes registered synchronizers from `SynchronizeForInput` on every plugin input build for synchronous update modes, so class live state also reflects over properties before event dispatch.
- Existing `PAL-0002` covered interface-shaped `DispatchProxy` property access, and `PAL-0004` covered convention event adapter reflection. This finding is separate: it is the non-interface/class live-state synchronization path.

## Impact

Class-shaped settings are the non-proxy alternative for live kernel state, but their synchronization still pays repeated reflection metadata discovery and reflection get/set overhead on plugin input construction, flushes, external setting refresh, and `ModifyAsync`. High-frequency plugin hooks with several live settings will spend work synchronizing the state object even when the live-setting shape is stable for the lifetime of the kernel.

## Measurement idea

Add a BenchmarkDotNet benchmark with a class-shaped live state containing 1, 5, and 20 properties. Measure `InstalledKernel.ModifyAsync`, `FlushUpdatesAsync`, and repeated hook input construction under synchronous update mode, tracking allocated bytes and time before and after caching metadata/accessors.

## Suggested fix direction

Cache class live-state metadata per state type: selected setting names, property types, and compiled getter/setter delegates. Reuse the cached shape for draft creation, extract, copy, pull, and push paths so hot synchronization avoids repeated `GetProperties`, `Attribute.IsDefined`, `PropertyInfo.GetValue`, and `PropertyInfo.SetValue` calls.
