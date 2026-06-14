---
id: CMP-0007
area: completeness
status: fixed_pending_verification
priority: medium
title: Runnable addendum examples do not exercise production JSON package upload
dedup_key: cmp-runnable-addendum-json-upload-example-coverage
created_at: 2026-06-12T22:03:19.7712527+00:00
created_by: Codex completeness auditor
created_commit: 
updated_at: 2026-06-13T06:13:56.8791192+00:00
claimed_by: worker
claimed_at: 2026-06-13T06:12:06.6519887+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T06:13:56.8791192+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# CMP-0007: Runnable addendum examples do not exercise production JSON package upload

## Claim

The runnable addendum example suite does not prove the documented production upload path. The docs say production servers should install serialized JSON package data through `PluginPackageJsonSerializer.Export` and `InstallJsonAsync`, but the examples that `check-docs-smoke.ps1` runs only install the generated in-memory package factory directly.

## Why this matters

The JSON package boundary is the security-relevant feature-completeness story for plugins: production upload must not load arbitrary plugin DLLs. Without a runnable example in the smoke suite, docs can keep describing the production boundary while the maintained examples only exercise trusted development-time install.

## Evidence

- `docs/Specs/Addendum/Addendum.md:29` says the generated factory can create a `PluginPackage` and `PluginPackageJsonSerializer.Export` converts it to the JSON envelope used for upload.
- `docs/Specs/Addendum/Addendum.md:754` and `docs/Specs/Addendum/Addendum.md:755` state the upload path is `PluginPackageJsonSerializer.Export(FireDamagePluginPackage.Create())` followed by server-side `InstallJsonAsync` validation.
- `examples/Addendum/DotBoxd.Kernels.AddendumExamples/AddendumExampleRunner.cs` runs the documented example set, but it has no JSON upload/install example.
- `examples/Addendum/DotBoxd.Kernels.AddendumExamples/DotBoxd.Kernels.AddendumExamples.csproj` references `DotBoxd.Plugins.Analyzer`, `DotBoxd.Plugins`, and the server abstractions, but not `DotBoxd.Kernels.Serialization.Json`, so this runnable example project cannot exercise `InstallJsonAsync` without additional setup.
- `scripts/check-docs-smoke.ps1` runs the addendum and local plugin examples, so the current smoke coverage also misses the JSON upload path.

## Suggested test or benchmark

Add a runnable example that exports `FireDamagePluginPackage.Create()` to JSON, installs it with `server.InstallJsonAsync`, publishes a matching event, and asserts/prints the expected message. Include it in `AddendumExampleRunner` and keep `check-docs-smoke.ps1` running it.

## Suggested fix direction

Add a focused `JsonUploadExample` to `examples/Addendum/DotBoxd.Kernels.AddendumExamples`, reference `DotBoxd.Kernels.Serialization.Json`, and update `examples/Addendum/README.md` plus `docs/Specs/Addendum/Examples.md` so the production upload path is part of the maintained example set.

## Scope boundaries

Do not replace the local generated factory examples; keep them as SDK/dev-tooling examples. This finding only asks for an additional runnable production-boundary example.

## Deduplication key

`cmp-runnable-addendum-json-upload-example-coverage`

## Verification checklist

- [ ] Addendum examples include a JSON export plus `InstallJsonAsync` path.
- [ ] The example proves a hook execution after JSON install.
- [ ] `check-docs-smoke.ps1` exercises the new example through the existing addendum run.
- [ ] Existing local generated factory examples remain available.
