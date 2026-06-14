---
id: ALG-0019
area: perf_algorithm
status: open
priority: medium
title: Live setting updates coerce and validate values twice
dedup_key: algorithm/plugins/live-settings/update-double-coerce-validate
created_at: 2026-06-13T06:24:35.1587647+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:24:35.1587647+00:00
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

# ALG-0019: Live setting updates coerce and validate values twice

## Claim

Plugin live-setting updates coerce and range-validate values in the store, then call the setting slot API that repeats the same coercion and validation before storing the value.

## Evidence

- `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:112` through `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:117` handles single-setting updates by calling `CoerceAndValidate(name, value)` and then `coerced.Setting.SetObject(coerced.Value)`.
- `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:207` through `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:215` resolves the setting, coerces through `LiveSettingTypeConverter.CoerceClr(...)`, and validates ranges through `ValidateRangeValue(...)`.
- For manifest-backed slots, `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:238` through `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:249` repeats type-specific coercion and `ValidateRangeValue(...)` inside `LiveSettingSlot.SetObject` before taking the slot lock.
- Batch updates do the same double work: `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:120` through `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:127` calls `CoerceAndValidate(values)`, whose loop at `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:197` through `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:204` pre-validates every value, then calls each slot's `SetObject` again.
- Plugin runtime update entrypoints route through this path at `src/DotBoxd.Plugins/InstalledKernel.cs:178` through `src/DotBoxd.Plugins/InstalledKernel.cs:192` and `src/DotBoxd.Plugins/InstalledKernel.cs:203` through `src/DotBoxd.Plugins/InstalledKernel.cs:231`.
- Existing live-setting performance findings cover proxy reflection (`PAL-0002`) and class-shaped state reflection sync (`PAL-0032`). This finding is separate: even with cached accessors, the store still performs duplicate conversion and range validation per update.

## Impact

Hosts can apply live setting changes during gameplay/event processing and plugin tooling can batch-update several settings at once. Numeric range checks parse/convert values repeatedly, and `SetMany` doubles the work for every updated setting while holding the store lock. This makes configuration updates and async live-state flushes slower than necessary and increases contention around the live-setting store.

## Suggested fix

Split validation from storage. After `CoerceAndValidate` returns a trusted typed value, store it through an internal slot method that accepts an already-coerced value and does not repeat conversion/range checks. Alternatively, move all validation into the slot and make `SetMany` first collect resolved slots plus coerced values without calling both layers. Keep atomic batch semantics and preserve the current unknown-setting failure behavior.

## Benchmark/allocation test idea

Add a plugin live-setting update benchmark with 1, 5, and 20 manifest-backed settings, including integer/long/double ranges. Measure `InstalledKernel.ModifySettingsAsync`, interface-shaped `ModifyAsync`, and class-shaped `ModifyAsync` for repeated updates. Assert each setting is coerced and range-validated once per update, not once in the store and again in the slot.
