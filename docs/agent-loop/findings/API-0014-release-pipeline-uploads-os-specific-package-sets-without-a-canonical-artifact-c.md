---
id: API-0014
area: api_coherence
status: fixed_pending_verification
priority: medium
title: Release pipeline uploads OS-specific package sets without a canonical artifact check
dedup_key: api/package-release/os-matrix-package-artifact-identity
created_at: 2026-06-12T22:33:28.7636517+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T06:10:54.0426212+00:00
claimed_by: worker
claimed_at: 2026-06-13T06:10:07.8314907+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T06:10:54.0426212+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0014: Release pipeline uploads OS-specific package sets without a canonical artifact check

# Release pipeline uploads OS-specific package sets without a canonical artifact check

## Problem

The CI package job runs in a Windows, Ubuntu, and macOS matrix, and each matrix leg runs `dotnet pack` for the same public DotBoxd.Kernels package IDs and versions. The workflow then uploads `artifacts/packages/*.nupkg` separately as `packages-${{ matrix.os }}`. The metadata gate checks each leg's packages independently, but no release gate chooses one canonical package set or proves that the three OS-built package sets are byte-for-byte identical.

## Why this matters

DotBoxd.Kernels's public NuGet packages should have one reproducible artifact set for a given package ID, version, and repository commit. If cross-OS packing produces different nupkg bytes, nuspec metadata, embedded paths, generated files, timestamps, or analyzer/package layout, the current release pipeline can still pass and leave maintainers with multiple valid-looking artifacts for the same version. A later publish step could upload whichever artifact set was downloaded first, making package provenance ambiguous and making release reproduction harder.

## Evidence

- `.github/workflows/ci.yml` defines `build-test-pack` with `matrix.os: [ windows-latest, ubuntu-latest, macos-latest ]`.
- The `Pack` step runs inside that matrix and writes `dotnet pack DotBoxd.Kernels.slnx --configuration Release --no-build --output artifacts/packages` on every OS.
- The `Upload package artifacts` step uploads the same package glob as `packages-${{ matrix.os }}`, producing three artifact sets for the same package IDs and versions.
- `scripts/check-package-metadata.ps1` validates fields, expected package IDs, repository commit, package layout, and prerelease policy for the packages in one directory, but it does not compare package hashes across matrix legs or designate a canonical publish source.

## Suggested fix

Split package publishing from cross-platform build/test validation. Either pack once in a canonical release job after all OS tests pass, or add a release gate that downloads the Windows/Linux/macOS package artifacts, normalizes only explicitly allowed non-deterministic metadata if needed, and fails unless each package ID/version has identical package bytes and nuspec/package entries across OSes. Publish only the canonical or verified-identical package set.

## Acceptance criteria

- [ ] CI/release has exactly one canonical package artifact set for each package ID/version, or a gate proves all matrix-built package sets are identical before upload/publish.
- [ ] The package metadata/release validation path fails if two OS legs produce different `.nupkg` contents for the same package ID/version.
- [ ] Release documentation states which package artifact set is publishable.

## Deduplication key

`api/package-release/os-matrix-package-artifact-identity`
