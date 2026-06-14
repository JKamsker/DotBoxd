---
id: API-0029
area: api_coherence
status: open
priority: medium
title: LiveValue range metadata is hidden behind internal construction
dedup_key: api/plugins/live-settings/ranged-live-value-authoring-missing
created_at: 2026-06-13T06:58:44.4361949+00:00
created_by: core-completeness-producer
created_commit: 
updated_at: 2026-06-13T06:58:44.4361949+00:00
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

# API-0029: LiveValue range metadata is hidden behind internal construction

## Claim

`DotBoxd.Plugins` exposes live-setting range metadata in the public manifest model and enforces it at runtime, but the public host-authored live-value API cannot create a built-in ranged live setting. The only `LiveValue<T>` constructor available to consumers accepts a name and initial value; the constructor that accepts a full `LiveSettingDefinition` is internal.

This leaves the package-facing live setting surface inconsistent: generated/imported package settings can carry `Min` and `Max`, while host-authored live values created through `PluginServer.BindValue<T>(...)` or `new LiveValue<T>(...)` cannot express the same contract without custom `ILiveSetting` code.

## Evidence

- `src/DotBoxd.Plugins/PluginManifest.cs` declares public `LiveSettingDefinition(string Name, string Type, object? DefaultValue, object? Min = null, object? Max = null)`.
- `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs` imports and exports `min` and `max` for live settings.
- `src/DotBoxd.Plugins/Runtime/Lifecycle/LiveSettingTypeConverter.cs` validates numeric range definitions and range values.
- `src/DotBoxd.Plugins/Runtime/LiveSettings.cs` exposes public `LiveValue<T>(string name, T value)` and public `LiveSettingStore(IEnumerable<ILiveSetting> settings)`.
- The only `LiveValue<T>` constructor that can carry a caller-supplied `LiveSettingDefinition` is `internal LiveValue(LiveSettingDefinition definition, T value)`.
- `src/DotBoxd.Plugins/PluginServer.cs` exposes `BindValue<T>(string name, T initialValue)` and always creates an unranged `LiveValue<T>`.

## Impact

A host using the public live setting API cannot attach the same numeric range contract that package manifests, JSON import/export, and generated plugin packages already support. To get ranged host-owned settings, consumers must either avoid the built-in `LiveValue<T>` type or implement `ILiveSetting` themselves while duplicating internal type conversion and validation behavior. That increases drift between generated package settings and host-authored settings.

## Suggested fix direction

Expose ranged live-setting construction through the supported API. Examples include `LiveValue<T>(string name, T value, T? min = default, T? max = default)` for supported numeric types, `PluginServer.BindValue<T>(..., T? min, T? max)`, or a public typed factory such as `LiveSettingDefinition.Create<T>(...)` plus a public `LiveValue<T>(LiveSettingDefinition definition)` constructor. Reuse the existing range validation path so package-imported and host-authored settings share behavior.

## Non-duplicates checked

`API-0026` covers hidden manifest vocabulary and suggested manifest construction helpers. This finding is distinct: the live-setting range fields themselves are already public, but the built-in public `LiveValue<T>` and `BindValue<T>` authoring path cannot populate them. Existing live-setting correctness/performance findings cover validation behavior and conversion cost, not missing public range authoring surface.

## Deduplication key

`api/plugins/live-settings/ranged-live-value-authoring-missing`

## Verification checklist

- [ ] Public code can create a built-in live setting with numeric min/max constraints.
- [ ] Public `PluginServer.BindValue` or an equivalent supported factory preserves range metadata in `Definition`.
- [ ] Runtime updates enforce the same range checks for host-authored and manifest-backed settings.
- [ ] Unsupported ranges on non-numeric settings still fail with the existing diagnostic behavior.
