---
id: COR-0038
area: correctness
status: open
priority: high
title: Plugin package JSON import returns unvalidated packages
dedup_key: security/plugin-package-json/import/unvalidated-package-object
created_at: 2026-06-12T22:48:36.7805462+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T22:48:36.7805462+00:00
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

# COR-0038: Plugin package JSON import returns unvalidated packages

## Summary

`PluginPackageJsonSerializer.Import` parses untrusted plugin package JSON and returns a public `PluginPackage` before the semantic package validator has run. The only validator that checks manifest/module binding, entrypoint publicness, effect declarations, live setting ranges, and subscription/kernel consistency is internal and is called by `PluginServer.InstallAsync`, so consumers that deserialize packages for review, storage, policy decisions, or custom install pipelines can accidentally trust an unvalidated package object.

## Evidence

- `src/SafeIR.Serialization.Json/PluginPackageJsonSerializer.cs` exposes public `PluginPackageJsonSerializer.Import(string json)` and returns `ReadPackage(document.RootElement)` after JSON shape parsing.
- `src/SafeIR.Serialization.Json/PluginPackageJsonSerializer.cs` `ReadPackage` constructs `PluginPackage.Create(manifest, module, entrypoints)` directly; it does not call package semantic validation before returning.
- `src/SafeIR.Plugins/Runtime/PluginPackageValidator.cs` is `internal` and enforces the security-relevant package checks: manifest plugin id must match module id, module metadata must bind to the manifest plugin id and kernel, entrypoints must exist and be public, manifest effects must parse to known effects, live settings must have supported types/ranges, and subscriptions must bind to the module kernel.
- `src/SafeIR.Plugins/PluginServer.cs` calls `PluginPackageValidator.Validate(package)` and `ValidatePrepared(...)` inside `InstallAsync`, but callers using `PluginPackageJsonSerializer.Import` directly cannot run the same validator because it is not public.
- This is separate from `COR-0016`, which covers the default policy used during install. This finding is about the pre-install deserialization trust boundary and the absence of a public validated-import result.

## Impact

Plugin package JSON is an upload/trust boundary. A malformed or misleading package can be deserialized into the same public `PluginPackage` shape as a validated package, with attacker-controlled manifest effects, contract text, entrypoints, and module metadata still unchecked. Hosts that inspect imported packages before install, cache them, show approval UI, compute policy from declared effects, or implement custom install flows can make decisions from data that the official installer would later reject.

## Test idea

Add a public API test that imports JSON where `manifest.pluginId` differs from `module.id` or where `entrypoints.handle` names a non-public/missing function. The safe expectation should be that the public validated import path rejects the package before returning a trustable `PluginPackage`, or returns a result that clearly carries validation diagnostics and cannot be mistaken for a validated package.

## Suggested fix

Add a public validated import boundary, such as `PluginPackageJsonSerializer.ImportValidated(...)` or a public `PluginPackageValidationResult`, and have `InstallJsonAsync` use it. Alternatively make `Import` perform the semantic package validation before returning and provide a clearly named lower-level parse-only API for tooling that intentionally wants unchecked JSON model construction.
