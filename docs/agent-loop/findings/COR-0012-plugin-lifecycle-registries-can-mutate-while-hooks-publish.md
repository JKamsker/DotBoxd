---
id: COR-0012
area: correctness
status: open
priority: medium
title: Plugin lifecycle registries can mutate while hooks publish
dedup_key: correctness/plugins/lifecycle/hook-registry-unsynchronized-concurrent-mutation
created_at: 2026-06-12T22:02:34.3138869+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T22:02:34.3138869+00:00
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

# COR-0012: Plugin lifecycle registries can mutate while hooks publish

## Claim

Plugin hook and kernel registries mutate ordinary `Dictionary` and `List` instances without synchronization while public APIs can publish events, install/uninstall kernels, and add handlers concurrently. A host that changes plugin lifecycle state while events are being published can hit collection races or inconsistent handler sets.

## Evidence

`src/SafeIR.Plugins/Runtime/HookRegistry.cs` stores pipelines in `Dictionary<Type, object> _pipelines` and each `HookPipeline<TEvent>` stores `_filters` and `_handlers` as `List<...>`. `On`/`UseKernel` mutate those collections, while `PublishAsync` reads `_pipelines` and iterates `_filters` / `_handlers` without a snapshot or lock:

```csharp
foreach (var filter in _filters) { ... }
foreach (var handler in _handlers) { ... }
```

`src/SafeIR.Plugins/PluginServer.cs` also exposes `InstallAsync`, `Uninstall`, and `Kernels.Get<T>` over a plain `Dictionary<string, InstalledKernel> _kernels`. `KernelRegistry.Add` can revoke and replace entries while `GetByKernelType` or `Remove` read/write the same dictionary. None of these public lifecycle/publish paths document single-thread affinity, and plugin execution itself is asynchronous, so concurrent publish and lifecycle changes are realistic.

Existing plugin lifecycle tests cover sequential uninstall/reinstall and uninstall while a handler is blocked, but they do not exercise concurrent `UseKernel`/`PublishAsync` or concurrent install/uninstall/get operations.

## Suggested test

Add a plugin concurrency test that starts a `PublishAsync` with a blocking filter or handler, then concurrently calls `server.Hooks.On<TEvent>().UseKernel(...)` or `server.InstallAsync` / `server.Uninstall` on the same server. The test should assert no `InvalidOperationException`, `KeyNotFoundException`, or corrupted lifecycle state occurs, and that each publish observes a stable snapshot of handlers.

## Expected behavior

Plugin server lifecycle operations should be serialized or implemented with immutable snapshots/concurrent collections. A publish should observe a stable pipeline snapshot, and install/uninstall/get should not race on `KernelRegistry` internals.

## Deduplication key

`correctness/plugins/lifecycle/hook-registry-unsynchronized-concurrent-mutation`
