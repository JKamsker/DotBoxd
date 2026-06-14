---
id: CMP-0002
area: completeness
status: fixed_pending_verification
priority: medium
title: Execution-mode example changes manifest mode instead of host dispatch mode
dedup_key: completeness/plugins/execution-mode/example-manifest-mode-does-not-drive-dispatch
created_at: 2026-06-12T20:38:56.9027789+00:00
created_by: feature-completeness-scout
created_commit: 
updated_at: 2026-06-12T20:59:50.8295879+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:58:24.1575595+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:59:50.8295879+00:00
fixed_commit: working-tree
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# CMP-0002: Execution-mode example changes manifest mode instead of host dispatch mode

## Claim

The addendum execution-mode example changes `PluginManifest.Mode` for each iteration, but plugin execution uses the `PluginServer` execution mode instead. As a result, the example says it is demonstrating interpreted/compiled/auto modes while leaving the runtime request mode at the server default.

## Evidence

- `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/ExecutionModeExample.cs` loops over `ExecutionMode.Interpreted`, `Compiled`, and `Auto`, then calls `await server.InstallAsync(WithMode(FireDamagePluginPackage.Create(), mode));`.
- `PluginServer.Create` defaults its server-level `executionMode` parameter to `ExecutionMode.Auto` and passes that value into `InstalledKernel` during `InstallAsync`.
- `InstalledKernel.ExecutePreparedAsync` executes with `new SandboxExecutionOptions { Mode = _executionMode }`, which is the server-level mode captured at install time, not `Package.Manifest.Mode`.
- `tests/DotBoxd.Kernels.Tests/Misc06/PluginPackageValidationTests.cs` contains `Manifest_compiled_mode_does_not_force_plugin_compiler_dispatch`, which confirms manifest `Mode = ExecutionMode.Compiled` intentionally does not force compiled execution.

## User impact

A reader running the addendum examples can believe package manifest mode selects plugin execution mode, but the actual host policy remains unchanged. That makes interpreted/compiled/auto lifecycle guidance hard to trust and hides the correct way to configure or observe plugin execution modes.

## Suggested acceptance test

Add an example or test that installs/runs the fire-damage package under each intended mode by configuring the server, for example `PluginServer.Create(messages, executionMode: mode)`, then asserts `kernel.ExecutionObservations` records the expected `RequestedMode` for `ShouldHandle` and `Handle`. The test should fail if only `PluginManifest.Mode` is changed while requested mode remains the server default.

## Smallest fixable slice

Update `ExecutionModeExample` to pass `executionMode: mode` to `PluginServer.Create` and print/assert `kernel.ExecutionObservations` instead of mutating `Manifest.Mode` as if it controls dispatch. If manifest mode is only advisory metadata, add a sentence to the example/docs stating that host policy controls runtime mode.
