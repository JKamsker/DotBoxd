---
id: COR-0044
area: correctness
status: claimed
priority: high
title: Package-producing workflow emits unsigned unattested NuGet artifacts
dedup_key: security/release-packaging/nuget/unsigned-unattested-artifacts
created_at: 2026-06-12T23:04:43.7913439+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T23:22:17.2538613+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:22:17.2538613+00:00
claim_branch: workflow-work
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0044: Package-producing workflow emits unsigned unattested NuGet artifacts

## Claim

The package-producing workflow creates and uploads NuGet artifacts for release branches and tags, but neither the workflow nor the package metadata gate requires package signing, signature verification, or artifact provenance/attestation. The release gate can prove selected nuspec metadata and package layout at CI time, but the uploaded `.nupkg` files remain unsigned trust artifacts.

## Evidence

- `.github/workflows/ci.yml` runs on pushes to `release/**` branches and `v*` tags, runs `dotnet pack SafeIR.slnx --configuration Release --no-build --output artifacts/packages`, checks the packages, and uploads `artifacts/packages/*.nupkg`.
- `.github/workflows/ci.yml` has no signing, `dotnet nuget verify --signatures`, artifact attestation, provenance upload, or trusted-publisher verification step after packing.
- `scripts/check-package-metadata.ps1` opens each `.nupkg` and validates package ID, version, license, repository URL, repository commit, dependency prerelease policy, and selected payload entries, but it never checks for a NuGet package signature, a `.signature.p7s` entry, a trusted signer, or external provenance evidence.
- This is distinct from `COR-0035`, which covers unexpected package payloads, from `API-0014`, which covers OS-specific package sets without a canonical artifact check, and from `COR-0040`, which covers mutable GitHub Action tags in package-producing jobs. This finding is about cryptographic tamper evidence for the package artifact itself after it has been packed and metadata-checked.

## Risk

Release `.nupkg` files are the artifacts downstream publishers and consumers trust. Without a package signature or verifiable build provenance, a package copied from CI artifacts, release assets, or a downstream publishing handoff cannot be authenticated as the exact artifact produced by the checked Safe-IR commit and release workflow. A replacement package can preserve visible nuspec fields such as ID, version, repository URL, and repository commit while changing DLL contents; current checks do not leave consumers or later publish automation with cryptographic evidence that detects that substitution outside the original CI run.

## Suggested test

Add a release/package validation test or script fixture that runs the package gate against an unsigned `.nupkg` and expects failure when release mode is enabled. Add a positive fixture or integration path for a signed package or an attested package set, and include a workflow lint that fails if package-producing release jobs omit the signing/attestation step.

## Expected behavior

Release package artifacts should be signed or accompanied by verifiable OIDC provenance before upload, and release validation should fail closed when a package lacks the required signature or attestation. Downstream publish automation should verify that evidence before publishing or promoting artifacts.

## Suggested fix

Add a release-only signing or attestation phase after `dotnet pack` and before artifact upload. Extend `scripts/check-package-metadata.ps1` or add a companion release gate to verify NuGet signatures with trusted signers or to verify GitHub artifact attestations/provenance for every `.nupkg` in the package directory.
