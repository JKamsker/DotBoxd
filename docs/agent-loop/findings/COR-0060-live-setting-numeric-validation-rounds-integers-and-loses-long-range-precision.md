---
id: COR-0060
area: correctness
status: open
priority: medium
title: Live setting numeric validation rounds integers and loses long range precision
dedup_key: correctness:live-settings-type-exact-numeric-validation
created_at: 2026-06-13T06:28:37.9335392+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T06:28:37.9335392+00:00
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

# COR-0060: Live setting numeric validation rounds integers and loses long range precision

## Evidence

`LiveSettingTypeConverter.CoerceClrCore` coerces non-string int and long values with `Convert.ToInt32` and `Convert.ToInt64` (`src/DotBoxd.Plugins/Runtime/Lifecycle/LiveSettingTypeConverter.cs`). Those conversions round fractional `double`/`decimal` inputs instead of rejecting them, so programmatic defaults or `LiveSettingStore.SetObject` updates can turn values such as `1.5` into an accepted integer setting.

The same converter validates numeric ranges through `Number(value)`, which uses `Convert.ToDouble` for all numeric types. That loses exactness for `long` values above 2^53. `PluginPackageJsonSerializer` reads `long` live-setting defaults and bounds as `Int64` first (`src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs`), but range validation then compares them as doubles.

`LiveSettingStore.FromDefinitions` and `LiveSettingStore.SetObject` rely on this converter for default validation and runtime updates (`src/DotBoxd.Plugins/Runtime/LiveSettings.cs`).

## Impact

Invalid integer live-setting values can be silently accepted after rounding, and exact `long` min/max checks can accept or reject the wrong values near large 64-bit boundaries. For example, a value just below a large `long` minimum can compare equal after conversion to `double` and pass validation.

## Fix direction

Validate numeric settings using the declared setting type. Reject fractional input for int/long settings, compare long ranges with exact integer arithmetic, and keep double range validation finite-double-specific. Add tests for fractional programmatic integer updates and long bounds around 9,007,199,254,740,993.
