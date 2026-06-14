---
id: PAL-0051
area: perf_alloc
status: open
priority: medium
title: Plugin kernel inputs re-coerce live settings per dispatch
dedup_key: alloc/plugins/live-settings/input-recoerce-sandbox-values
created_at: 2026-06-13T06:56:20.2303574+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:56:20.2303574+00:00
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

# PAL-0051: Plugin kernel inputs re-coerce live settings per dispatch

## Summary

Plugin kernel input construction converts every manifest live setting into a fresh `SandboxValue` on every dispatch, even when the live setting has not changed since the previous event.

## Evidence

- `src/DotBoxd.Plugins/InstalledKernel.cs:285` builds input for every `ShouldHandleAsync`, `HandleAsync`, and hook `InvokeAsync` call and passes `Manifest.LiveSettings` plus the shared `LiveSettingStore` into `PluginKernelInputBuilder.Build`.
- `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:36` and `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:52` call `value.ToSandboxValue(...)` for the single-live-setting input case.
- `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:73` and `src/DotBoxd.Plugins/Runtime/Input/PluginKernelInputBuilder.cs:88` call `value.CopySandboxValues(...)` for multi-value inputs.
- `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:132` through `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:147` take the store lock and call each setting slot's `ToSandboxValue()` while building the input array.
- `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:49` through `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:50` and `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:235` through `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:236` route each setting through `LiveSettingTypeConverter.ToSandboxValue(...)`.
- `src/DotBoxd.Plugins/Runtime/Lifecycle/LiveSettingTypeConverter.cs:108` through `src/DotBoxd.Plugins/Runtime/Lifecycle/LiveSettingTypeConverter.cs:115` re-coerces the current CLR value and creates a new scalar `SandboxValue` wrapper for every conversion.
- Existing `PAL-0040` covers the working-array plus defensive-snapshot copy for multi-value inputs. Existing `ALG-0019` covers double coercion during live-setting updates. This finding is the steady-state dispatch cost of re-coercing and re-boxing unchanged settings before sandbox execution.

## Impact

High-frequency plugin hosts pass through input construction for every event and for every installed kernel. A package with several stable live settings currently pays repeated lock contention, type coercion, numeric conversion, and scalar wrapper allocation for settings whose sandbox representation could be unchanged across thousands of events. This overhead remains even if the multi-value input copy issue is fixed, and it applies to single-live-setting inputs that avoid the list-building path entirely.

## Suggested direction

Cache each setting's `SandboxValue` representation inside the live setting slot and refresh it only when the setting changes. `CopySandboxValues` and the single-setting input path should copy or return the cached sandbox value under the existing store/slot synchronization instead of coercing the current object again. Preserve update-time range validation and ensure cached string/path-sized values still participate in normal runtime budget charging when the resulting input is charged.

## Benchmark/allocation test idea

Add a plugin input allocation benchmark with 1, 5, and 20 manifest live settings and no changing setting values, publishing 1,000, 10,000, and 100,000 events through `ShouldHandleAsync` or hook `InvokeAsync`. Measure bytes allocated and time before sandbox execution, and assert steady-state live-setting input construction does not call `LiveSettingTypeConverter.ToSandboxValue` or allocate a new scalar `SandboxValue` per setting per event.
