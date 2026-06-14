---
id: PAL-0040
area: perf_alloc
status: open
priority: medium
title: Plugin kernel input building double-copies multi-value inputs
dedup_key: alloc/plugins/kernel-input/multivalue-double-copy
created_at: 2026-06-13T06:24:36.4695234+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:24:36.4695234+00:00
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

# PAL-0040: Plugin kernel input building double-copies multi-value inputs

## Claim

Plugin kernel input construction allocates a working `SandboxValue[]` for multi-value event/live-setting inputs and then `ListValue` defensively snapshots that same array into another array, so every multi-value hook input pays duplicate array allocation/copy before sandbox execution.

## Evidence

- `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:67` allocates a `SandboxValue[]` when adapter-provided event values must be combined with live settings.
- `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:68` through `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:73` copies event values and live-setting values into that working array, then `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:74` passes it to `SandboxValue.FromList(values, values[0].Type)`.
- The writer fast path has the same shape: `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:83` through `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:91` allocates/fills a working array before calling `SandboxValue.FromList(...)`.
- `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:51` through `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:52` constructs a `ListValue`, and `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:116` through `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:120` defensively snapshots the constructor input with `ModelCopy.List(...)`.
- `src/DotBoxd.Kernels/Model/ModelCopy.cs:7` through `src/DotBoxd.Kernels/Model/ModelCopy.cs:10` implements that snapshot as `values.ToArray()` plus `ReadOnlyCollection<T>`.
- `tests/DotBoxd.Kernels.Tests/Misc05/PluginInputAllocationTests.cs:7` through `tests/DotBoxd.Kernels.Tests/Misc05/PluginInputAllocationTests.cs:19` only guards against enumerating an index-only event value list when live settings exist; it does not assert allocation count or prevent the working-array plus snapshot-array pattern.
- Existing `COR-0008` covers the correctness problem of modeling heterogeneous plugin inputs as homogeneous lists. This finding is the allocation cost that remains for multi-value input construction independent of that type-modeling issue.

## Impact

High-frequency plugin hosts build kernel input for every event that reaches an installed kernel. Any event with multiple values, or a single event value plus live settings, currently allocates and copies one temporary array and then another immutable snapshot array before `ShouldHandle`/`Handle` execution can begin. With several kernels or high event rates, that avoidable allocation becomes plugin dispatch overhead rather than sandbox work.

## Suggested fix

Provide an internal owned-snapshot construction path for plugin/runtime code that has just allocated and filled an exact-size array and will not expose it for mutation, for example an internal `SandboxValue.FromOwnedList` or `ListValue` factory. Keep defensive copies for public APIs. If the tuple-input correctness issue is fixed with a dedicated entrypoint tuple representation, make that representation accept an owned exact-size array so plugin input construction allocates only once per event.

## Benchmark/allocation test idea

Add a plugin input allocation benchmark with adapters producing 1, 5, and 20 event values, with 0, 1, and 5 live settings, using both `IPluginEventAdapter<T>` and `IPluginEventValueWriter<T>`. Publish 1,000 to 100,000 events and measure bytes allocated before sandbox execution. Assert multi-value input construction does not allocate both a working array and a second snapshot array.
