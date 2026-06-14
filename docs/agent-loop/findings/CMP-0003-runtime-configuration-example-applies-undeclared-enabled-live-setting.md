---
id: CMP-0003
area: completeness
status: fixed_pending_verification
priority: medium
title: Runtime configuration example applies undeclared Enabled live setting
dedup_key: completeness/plugins/runtime-configuration/enabled-setting-not-in-fire-damage-package
created_at: 2026-06-12T20:38:58.1978226+00:00
created_by: feature-completeness-scout
created_commit: 
updated_at: 2026-06-12T21:04:06.0344407+00:00
claimed_by: implementer
claimed_at: 2026-06-12T21:02:57.9890793+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T21:04:06.0344407+00:00
fixed_commit: working-tree
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# CMP-0003: Runtime configuration example applies undeclared Enabled live setting

## Claim

The addendum runtime-configuration example applies an `Enabled` live setting to the generated fire-damage package, but the sample `FireDamageKernel` in this worktree only declares `DamageType` and `MinDamage` as live settings. The example runner therefore includes configuration that does not match the installed package manifest.

## Evidence

- `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/RuntimeConfigurationExample.cs` builds `config.Settings` with `Enabled`, `DamageType`, and `MinDamage`, then calls `await installed.ModifySettingsAsync(config.Settings);`.
- `examples/LocalPlugin/DotBoxd.Kernels.PluginLocal/FireDamageKernel.cs` declares `[LiveSetting]` only on `DamageType` and `MinDamage`; it has no `Enabled` live setting and does not gate `ShouldHandle` on `Enabled`.
- The generator tests for the local-style fire-damage kernel assert generated live settings for `DamageType` and `MinDamage`, not `Enabled`, in `tests/DotBoxd.Kernels.Tests/PluginAnalyzer/Generated/PluginAnalyzerTests.Generator.cs`.
- `LiveSettingStore.SetMany` rejects unknown setting names while applying a batch, so `Enabled` is not a harmless ignored configuration key.

## User impact

The public addendum example for loading runtime configuration is not coherent with the flagship local plugin package. Operators copying the example can include an `Enabled` setting that the package does not declare, causing configuration application to fail instead of demonstrating live settings.

## Suggested acceptance test

Add a smoke test that `AddendumExampleRunner.RunAsync()` or at least `RuntimeConfigurationExample.RunAsync()` completes successfully. Also assert that every key in the example configuration exists in `FireDamagePluginPackage.Create().Manifest.LiveSettings` before calling `ModifySettingsAsync`.

## Smallest fixable slice

Either add `[LiveSetting] public bool Enabled { get; set; } = true;` to the fire-damage sample kernel and include it in `ShouldHandle`, or remove `Enabled` from `RuntimeConfigurationExample` and its default configuration so the example only applies declared live settings.
