---
id: COR-0040
area: correctness
status: claimed
priority: high
title: Release workflow uses mutable action tags for package-producing jobs
dedup_key: security/release-workflow/actions/mutable-major-tags
created_at: 2026-06-12T22:52:55.6002158+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T23:22:15.5691852+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:22:15.5691852+00:00
claim_branch: workflow-work
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0040: Release workflow uses mutable action tags for package-producing jobs

# Release workflow uses mutable action tags for package-producing jobs

## Claim

The package-producing CI workflow runs on release branches and `v*` tags while using GitHub Actions pinned only to mutable major-version tags. The same job builds, packs, validates, and uploads NuGet package artifacts, so release artifact production depends on action code that is not fixed by repository commit or package metadata checks.

## Evidence

- `.github/workflows/ci.yml` runs on pushes to `release/**` branches and `v*` tags.
- `.github/workflows/ci.yml` uses `actions/checkout@v4`, `actions/setup-dotnet@v4`, and `actions/upload-artifact@v4` rather than full commit SHAs.
- The same workflow restores, builds, tests, packs with `dotnet pack`, runs `scripts/check-package-metadata.ps1`, and uploads `artifacts/packages/*.nupkg` as release artifacts.
- `scripts/check-package-metadata.ps1` verifies nuspec metadata, repository URL, package IDs, versions, expected repository commit, and selected package entries, but it cannot attest which GitHub Action implementations executed the checkout, SDK setup, or artifact upload steps.
- This is distinct from `COR-0035`, which covers unexpected package payloads inside the `.nupkg`, and from `API-0014`, which covers uploading OS-specific package sets without one canonical artifact. This finding is about the workflow supply-chain identity for the actions that produce and publish those artifacts.

## Risk

Release packages are downstream trust artifacts. If an upstream action tag is moved, compromised, or resolves differently over time, a release-tag run can execute different checkout/setup/upload code without any Safe-IR repository change. The package metadata may still claim the expected repository commit, but that commit does not cover the mutable action implementations that prepared the workspace, installed the SDK, or uploaded the final `.nupkg` files. This weakens release reproducibility and tamper evidence for package consumers.

## Suggested test

Add a workflow lint step or repository script that parses `.github/workflows/*.yml` and rejects `uses:` references that are not pinned to full commit SHAs for release/package-producing jobs. Include a fixture or negative case such as `actions/checkout@v4` and a positive case with a 40-character SHA.

## Expected behavior

Package-producing workflows should pin third-party actions to immutable full commit SHAs, with dependency automation or a documented review process used to update them. The release gate should fail if mutable action tags are introduced in jobs that pack, sign, validate, or upload release artifacts.

## Suggested fix

Replace the mutable major-version action refs in `.github/workflows/ci.yml` with reviewed full-length commit SHAs, and add a lightweight workflow/action pinning check to CI so future release pipeline changes remain fail-closed.
