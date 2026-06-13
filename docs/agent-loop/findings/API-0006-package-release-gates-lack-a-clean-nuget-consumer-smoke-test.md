---
id: API-0006
area: api_coherence
status: fixed_pending_verification
priority: medium
title: Package release gates lack a clean NuGet consumer smoke test
dedup_key: api/package-consumer-smoke/missing-clean-nuget-consumer
created_at: 2026-06-12T22:11:36.2964856+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T06:08:24.0051978+00:00
claimed_by: worker
claimed_at: 2026-06-13T06:01:44.8441977+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T06:08:24.0051978+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0006: Package release gates lack a clean NuGet consumer smoke test

# Package release gates lack a clean NuGet consumer smoke test

## Claim

The release/package readiness flow packs SafeIR packages and checks their nuspec metadata, but it never proves that a fresh consumer can restore the produced `.nupkg` files and compile the documented public API surface through `PackageReference`. Current smoke coverage uses project references and source-tree examples, so package layout, analyzer packaging, transitive dependencies, public namespaces, and README snippets can drift without a release gate failing.

## Why this matters

SafeIR is shipped as a set of public NuGet packages. A package can pass the current metadata gate while still being unusable from a clean application, for example because an analyzer package asset is misplaced, a public extension lives in the wrong namespace, a package dependency is missing, or the README requires a package combination that only works with project references. Package readiness should prove the artifacts users install are consumable, not only that the source tree builds.

## Evidence

- `.github/workflows/ci.yml:97` through `.github/workflows/ci.yml:102` packs to `artifacts/packages` and then runs only `scripts/check-package-metadata.ps1` against those `.nupkg` files.
- `scripts/check-package-metadata.ps1` validates nuspec fields, expected package IDs, repository/license/readme metadata, prerelease policy, and expected DLL/analyzer zip entries, but it does not run `dotnet restore`, `dotnet build`, or any compile check against a temporary consumer project using the packed packages.
- `scripts/check-docs-smoke.ps1:131` and `scripts/check-docs-smoke.ps1:132` run the addendum and local plugin examples with `--no-build`; they do not restore packages from `artifacts/packages`.
- The runnable examples use project references to product projects, for example `examples/Addendum/SafeIR.AddendumExamples/SafeIR.AddendumExamples.csproj:4` through `examples/Addendum/SafeIR.AddendumExamples/SafeIR.AddendumExamples.csproj:6` and `examples/PluginIpc/SafeIR.PluginIpc.Server/SafeIR.PluginIpc.Server.csproj:4` through `examples/PluginIpc/SafeIR.PluginIpc.Server/SafeIR.PluginIpc.Server.csproj:8`.
- `README.md:22` through `README.md:51` shows a minimal host snippet that depends on package/namespace composition across `SafeIR.Hosting`, `SafeIR.Runtime`, and the JSON import extension, while `README.md:120` through `README.md:143` documents plugin and IPC examples; none of those documented entry points are compiled from the produced NuGet packages in CI.
- Existing API findings cover specific missing namespaces/metadata/XML docs. This gap is separate: the release gate lacks a general clean-consumer proof for the package artifacts after those individual issues are fixed.

## Suggested acceptance test

Add a package consumer smoke script that runs after `dotnet pack` and before artifact upload. It should create one or more temporary projects with a local NuGet source pointing at `artifacts/packages`, add `PackageReference`s for the intended public package combinations, restore in locked/isolated mode where practical, and build snippets that cover at least:

- README minimal host usage with JSON import.
- HTTP addon host/policy setup.
- Plugin analyzer package consumption as an analyzer plus `SafeIR.Plugins` runtime package.
- Plugin JSON upload/export path through `SafeIR.Serialization.Json`.
- IPC preview package consumption from `SafeIR.Transport.Ipc.ShaRpc` with its allowed prerelease dependencies.

## Suggested fix direction

Create a focused `scripts/check-package-consumer-smoke.ps1` or extend the package metadata gate to generate temporary consumer projects. Keep the smoke small and compile-only; the goal is to validate package composition and public API discoverability from NuGet artifacts, not to duplicate behavioral test coverage.

## Scope boundaries

Do not replace existing source-tree tests, docs smoke, or package metadata checks. This finding only adds a clean package-consumption proof for public package readiness.

## Deduplication key

`api/package-consumer-smoke/missing-clean-nuget-consumer`
