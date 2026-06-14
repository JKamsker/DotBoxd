---
id: API-0010
area: api_coherence
status: fixed_pending_verification
priority: medium
title: Public packages do not ship symbol/source-link artifacts
dedup_key: api-package-symbol-source-link-artifacts
created_at: 2026-06-12T22:20:24.8148737+00:00
created_by: codex-api-producer
created_commit: 
updated_at: 2026-06-13T06:08:26.5406304+00:00
claimed_by: worker
claimed_at: 2026-06-13T06:01:47.0739351+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T06:08:26.5406304+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0010: Public packages do not ship symbol/source-link artifacts

## Claim
Public DotBoxd.Kernels packages are not configured or gated to produce NuGet symbol packages with source-link/debugging metadata, so package consumers cannot reliably step from a shipped package back to the exact source used for the release.

## Evidence
- `Directory.Build.props` sets shared package metadata and `ContinuousIntegrationBuild`, but it does not set `IncludeSymbols`, `SymbolPackageFormat`, `PublishRepositoryUrl`, or `EmbedUntrackedSources` for the public package projects.
- The source project files under `src/` only set target frameworks, references, and analyzer-specific packing; none override symbol/source-link package settings.
- `.github/workflows/ci.yml` packs to `artifacts/packages` and uploads only `artifacts/packages/*.nupkg`; it has no `.snupkg` artifact check or upload path.
- `scripts/check-package-metadata.ps1` validates nuspec metadata, expected package IDs, repository commit, dependencies, and package entries, but it does not require a matching symbol package or source-link metadata for each shipped library package.

## Impact
This is a public package readiness gap. When a consumer hits a DotBoxd.Kernels runtime, verifier, compiler, transport, or analyzer issue from NuGet, the release artifacts do not prove that debuggable symbols/source mapping were produced and retained for the package version. That makes incident diagnosis and API support materially harder after release.

## Better target
Configure public package projects to emit portable symbols and `.snupkg` packages with repository/source-link metadata, upload those artifacts in CI, and extend release/package validation so every shipped library package has a matching symbol package for the same ID/version.

## Acceptance test idea
After `dotnet pack DotBoxd.Kernels.slnx --configuration Release --no-build --output artifacts/packages`, a gate should fail unless every public non-analyzer package has both `.nupkg` and `.snupkg` artifacts for the same package ID/version and the symbols carry repository/source-link metadata for the current commit.

## Deduplication key
api-package-symbol-source-link-artifacts
