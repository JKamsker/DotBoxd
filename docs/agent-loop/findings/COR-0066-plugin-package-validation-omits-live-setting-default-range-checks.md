---
id: COR-0066
area: correctness
status: open
priority: medium
title: Plugin package validation omits live-setting default range checks
dedup_key: correctness/plugins/live-settings/default-range-validation
created_at: 2026-06-13T06:39:24.6508086+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T06:39:24.6508086+00:00
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

# COR-0066: Plugin package validation omits live-setting default range checks

## Claim

Plugin package validation checks that live-setting range definitions are well formed, but it does not check that each default value is inside its declared range.

## Evidence

`src/DotBoxd.Plugins/Runtime/PluginPackageValidator.cs` validates each live setting in `ValidateSetting(...)` by converting the type, converting the default with `LiveSettingTypeConverter.ToSandboxValue(...)`, and then calling `ValidateRange(setting, diagnostics)`.

`ValidateRange(...)` only calls `LiveSettingTypeConverter.ValidateRangeDefinition(setting)`, which verifies that ranges are numeric and that `Min <= Max`. It does not call `ValidateRangeValue(...)` for `setting.DefaultValue`.

`src/DotBoxd.Plugins/Runtime/LiveSettings.cs` does call `LiveSettingTypeConverter.ValidateRangeValue(...)` later from `LiveSettingStore.FromDefinitions(...)`, but that happens during `InstalledKernel` construction after `PluginServer.InstallAsync(...)` has already run package validation and prepared the module. `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs` also returns an imported package after `PluginPackageValidator.Validate(package)`, so a JSON package whose default is outside `min`/`max` can be returned as semantically valid by the public import path.

For example, a setting like `new LiveSettingDefinition("Limit", "int", 100, 0, 10)` has a supported type and a valid range definition, so package validation accepts it even though the initial value cannot satisfy the manifest contract.

## Impact

Package review/import and install validation disagree about whether the manifest is valid. Consumers using `PluginPackageJsonSerializer.Import(...)` for upload review or storage can accept an impossible package, and `PluginServer.InstallAsync(...)` can spend work preparing a module before failing when the live-setting store is constructed. The diagnostic also comes from a later runtime initialization boundary instead of the package validator that owns manifest correctness.

## Suggested test

Add package validation and JSON import tests with a numeric live setting whose default is outside its declared range, such as `defaultValue: 100`, `min: 0`, `max: 10`. Assert that `PluginPackageJsonSerializer.Import(...)` or `PluginServer.InstallAsync(...)` fails during package validation with `DBXK023`, before module preparation or kernel construction.

## Fix direction

In `PluginPackageValidator.ValidateSetting(...)`, coerce the default once using the declared live-setting type and validate it with `LiveSettingTypeConverter.ValidateRangeValue(...)`. Preserve the existing `DBXK022`/`DBXK023`/`DBXK024` diagnostics and keep the numeric precision fix from `COR-0060` as the source of truth for exact int/long/double comparisons.
