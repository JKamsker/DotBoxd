---
id: PAL-0004
area: perf_alloc
status: verified
priority: medium
title: Convention event adapter reflects and allocates per event
dedup_key: alloc/plugins/convention-event-adapter/reflection-array-per-event
created_at: 2026-06-12T21:00:44.3235962+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T21:40:39.7953888+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:33:30.7527803+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:37:25.5004534+00:00
fixed_commit: 
verified_by: independent-verifier
verified_at: 2026-06-12T21:40:39.7953888+00:00
verified_commit: 
duplicate_of: 
---

# PAL-0004: Convention event adapter reflects and allocates per event

## Claim

The convention plugin event adapter reflects over event properties and allocates a fresh event value array on every event dispatch, but there is no allocation benchmark covering this plugin hot path.

## Evidence

- `src/DotBoxd.Plugins/Runtime/InstalledKernel.cs:109`, `src/DotBoxd.Plugins/Runtime/InstalledKernel.cs:128`, and `src/DotBoxd.Plugins/Runtime/InstalledKernel.cs:149` route `ShouldHandleAsync`, `HandleAsync`, and combined invocation through `BuildInput` for each event.
- `src/DotBoxd.Plugins/Runtime/InstalledKernel.cs:260` calls `adapter.ToSandboxValues(e)` during input construction.
- `src/DotBoxd.Plugins/Runtime/PluginEventAdapterRegistry.cs:232` creates `var values = new SandboxValue[_properties.Count]` for each convention-adapted event.
- `src/DotBoxd.Plugins/Runtime/PluginEventAdapterRegistry.cs:237` loops over the cached `PropertyInfo` list, and `src/DotBoxd.Plugins/Runtime/PluginEventAdapterRegistry.cs:241` calls `property.GetValue(e)` for every property on every event.
- `src/DotBoxd.Plugins/Runtime/InstalledKernel.cs:294` then allocates another `SandboxValue[]` when live settings are also present, copying the event values before appending settings.
- Existing benchmarks focus on IPC allocation; there is no plugin event dispatch allocation benchmark for convention adapters with different event property counts and live-setting counts.

## Impact

Convention adapters are the default when no custom adapter is registered. High-frequency game or UI event streams will pay reflection invocation overhead and at least one fresh array per event, sometimes two arrays when live settings are included. This can dominate otherwise small plugin handlers and obscure the cost of the sandbox execution itself.

## Better target

Cache compiled property accessors per convention adapter and consider a direct input-builder path that writes event values and live settings into a single destination buffer. Custom adapters may still allocate, but the default convention path should avoid reflection `GetValue` and redundant event-value arrays.

## Benchmark idea

Add a BenchmarkDotNet plugin dispatch benchmark using convention-adapted events with 1, 5, and 20 scalar properties, with and without live settings. Measure allocated bytes and time for `ShouldHandleAsync`, `HandleAsync`, and combined invocation, comparing the current reflection path to cached delegates or a single-buffer input builder.
