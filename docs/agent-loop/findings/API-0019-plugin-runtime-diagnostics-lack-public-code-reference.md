---
id: API-0019
area: api_coherence
status: open
priority: medium
title: Plugin runtime diagnostics lack public code reference
dedup_key: api/plugins/runtime-sgp-diagnostics/missing-public-reference
created_at: 2026-06-12T23:19:37.7804462+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T23:19:37.7804462+00:00
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

# API-0019: Plugin runtime diagnostics lack public code reference

## Claim

`SafeIR.Plugins` emits runtime/plugin-package `SGP*` `SandboxDiagnostic` codes from package install, prepared-package validation, live-setting validation, and direct/hook entrypoint validation, but the public docs only cover analyzer-local diagnostics and do not provide a runtime plugin diagnostic code reference.

## Evidence

- `README.md:18` lists `SafeIR.Plugins` as a current public package for live plugin manifest, hook, kernel, and message-binding APIs.
- `src/SafeIR.Plugins/Runtime/PluginPackageValidator.cs` emits public validation diagnostics such as `SGP010`, `SGP011`, `SGP012`, `SGP013`, `SGP020`, `SGP021`, `SGP030`, `SGP031`, `SGP032`, `SGP040`, `SGP042`, and `SGP050` when an uploaded or generated plugin package is invalid.
- `src/SafeIR.Plugins/Runtime/PluginPreparedPackageValidator.cs` emits prepared-package diagnostics such as `SGP014`, `SGP033`, `SGP034`, `SGP035`, and `SGP041` after the package has been validated against the prepared SafeIR plan and registered event adapters.
- `src/SafeIR.Plugins/Runtime/Lifecycle/LiveSettingTypeConverter.cs` emits live-setting range diagnostics `SGP022`, `SGP023`, and `SGP024`, and `src/SafeIR.Plugins/Runtime/KernelEntrypointValidator.cs` emits `SGP031`, `SGP032`, and `SGP033` from runtime entrypoint checks.
- `docs/Specs/Addendum/Examples.md:270` only says package preparation fails with a policy diagnostic when `game.message.write` is missing; it does not name or explain any runtime `SGP*` package diagnostics.
- `docs/Specs/Addendum/Addendum.md:912` and `docs/Specs/Addendum/Addendum.md:940` show only `SGP001` and `SGP020` as local SDK/analyzer diagnostics, and `examples/Addendum/README.md:23` likewise mentions only the analyzer fixtures.
- Existing `API-0008` is scoped to `SafeIR.PluginAnalyzer` analyzer diagnostics, and existing `API-0018` is scoped to verifier `V-*` diagnostics; neither documents the runtime `SafeIR.Plugins` install/prepared-package diagnostic surface.

## Impact

Plugin hosts, upload UIs, and plugin authors can receive opaque `SGP*` failures from the public `SafeIR.Plugins` install and hook APIs without a maintained reference that distinguishes manifest identity errors, missing entrypoints, unsupported effects, event-adapter mismatches, live-setting range errors, and policy/setup problems. This makes package upload failures hard to triage and lets new runtime plugin diagnostics ship without user-facing guidance.

## Better target

Add a public `SafeIR.Plugins` runtime diagnostics reference linked from `README.md` and the addendum walkthrough. It should list each runtime `SGP*` code or code family with the emitting phase, likely cause, whether plugin authors or host operators must fix it, and a small remediation example.

## Release gate idea

Add a docs/package readiness check that extracts `SGP*` diagnostics from `src/SafeIR.Plugins` and fails when the runtime plugin diagnostics reference lacks an entry. Keep this separate from analyzer release-tracking checks so `SafeIR.PluginAnalyzer` and `SafeIR.Plugins` can document their overlapping `SGP` namespace without ambiguity.

## Scope boundaries

This does not change plugin validation behavior, analyzer diagnostics, verifier diagnostics, or the sandbox `SandboxErrorCode` reference. It only covers the missing public reference for runtime diagnostics emitted by the `SafeIR.Plugins` package.

## Deduplication key

`api/plugins/runtime-sgp-diagnostics/missing-public-reference`
