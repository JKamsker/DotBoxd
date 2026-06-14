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

`DotBoxd.Plugins` emits runtime/plugin-package `SGP*` `SandboxDiagnostic` codes from package install, prepared-package validation, live-setting validation, and direct/hook entrypoint validation, but the public docs only cover analyzer-local diagnostics and do not provide a runtime plugin diagnostic code reference.

## Evidence

- `README.md:18` lists `DotBoxd.Plugins` as a current public package for live plugin manifest, hook, kernel, and message-binding APIs.
- `src/DotBoxd.Plugins/Runtime/PluginPackageValidator.cs` emits public validation diagnostics such as `DBXK010`, `DBXK011`, `DBXK012`, `DBXK013`, `DBXK020`, `DBXK021`, `DBXK030`, `DBXK031`, `DBXK032`, `DBXK040`, `DBXK042`, and `DBXK050` when an uploaded or generated plugin package is invalid.
- `src/DotBoxd.Plugins/Runtime/PluginPreparedPackageValidator.cs` emits prepared-package diagnostics such as `DBXK014`, `DBXK033`, `DBXK034`, `DBXK035`, and `DBXK041` after the package has been validated against the prepared DotBoxd.Kernels plan and registered event adapters.
- `src/DotBoxd.Plugins/Runtime/Lifecycle/LiveSettingTypeConverter.cs` emits live-setting range diagnostics `DBXK022`, `DBXK023`, and `DBXK024`, and `src/DotBoxd.Plugins/Runtime/KernelEntrypointValidator.cs` emits `DBXK031`, `DBXK032`, and `DBXK033` from runtime entrypoint checks.
- `docs/Specs/Addendum/Examples.md:270` only says package preparation fails with a policy diagnostic when `game.message.write` is missing; it does not name or explain any runtime `SGP*` package diagnostics.
- `docs/Specs/Addendum/Addendum.md:912` and `docs/Specs/Addendum/Addendum.md:940` show only `DBXK001` and `DBXK020` as local SDK/analyzer diagnostics, and `examples/Addendum/README.md:23` likewise mentions only the analyzer fixtures.
- Existing `API-0008` is scoped to `DotBoxd.Plugins.Analyzer` analyzer diagnostics, and existing `API-0018` is scoped to verifier `V-*` diagnostics; neither documents the runtime `DotBoxd.Plugins` install/prepared-package diagnostic surface.

## Impact

Plugin hosts, upload UIs, and plugin authors can receive opaque `SGP*` failures from the public `DotBoxd.Plugins` install and hook APIs without a maintained reference that distinguishes manifest identity errors, missing entrypoints, unsupported effects, event-adapter mismatches, live-setting range errors, and policy/setup problems. This makes package upload failures hard to triage and lets new runtime plugin diagnostics ship without user-facing guidance.

## Better target

Add a public `DotBoxd.Plugins` runtime diagnostics reference linked from `README.md` and the addendum walkthrough. It should list each runtime `SGP*` code or code family with the emitting phase, likely cause, whether plugin authors or host operators must fix it, and a small remediation example.

## Release gate idea

Add a docs/package readiness check that extracts `SGP*` diagnostics from `src/DotBoxd.Plugins` and fails when the runtime plugin diagnostics reference lacks an entry. Keep this separate from analyzer release-tracking checks so `DotBoxd.Plugins.Analyzer` and `DotBoxd.Plugins` can document their overlapping `SGP` namespace without ambiguity.

## Scope boundaries

This does not change plugin validation behavior, analyzer diagnostics, verifier diagnostics, or the sandbox `SandboxErrorCode` reference. It only covers the missing public reference for runtime diagnostics emitted by the `DotBoxd.Plugins` package.

## Deduplication key

`api/plugins/runtime-sgp-diagnostics/missing-public-reference`
