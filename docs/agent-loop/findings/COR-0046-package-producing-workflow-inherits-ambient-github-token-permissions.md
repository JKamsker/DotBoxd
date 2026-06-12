---
id: COR-0046
area: correctness
status: claimed
priority: high
title: Package-producing workflow inherits ambient GitHub token permissions
dedup_key: security/release-workflow/github-token/ambient-default-permissions
created_at: 2026-06-12T23:11:50.3931360+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T23:22:18.8860941+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:22:18.8860941+00:00
claim_branch: workflow-work
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0046: Package-producing workflow inherits ambient GitHub token permissions

## Claim

The package-producing CI workflow does not declare explicit GitHub Actions `permissions`, so release branch and tag jobs inherit whatever repository or organization default `GITHUB_TOKEN` scopes are configured at run time. The same job restores, builds, tests, packs, validates, and uploads NuGet artifacts, so package production relies on an ambient token scope that is outside the workflow's reviewed source.

## Evidence

- `.github/workflows/ci.yml:3` through `.github/workflows/ci.yml:7` runs the workflow on pushes to `main`, `master`, `release/**`, and `v*` tags, plus pull requests.
- `.github/workflows/ci.yml:10` defines the `build-test-pack` job, but the workflow has no top-level or job-level `permissions:` stanza.
- `.github/workflows/ci.yml:27` through `.github/workflows/ci.yml:34` run restore/build/test, and `.github/workflows/ci.yml:36` through `.github/workflows/ci.yml:92` run repository scripts and release-readiness gates before packing.
- `.github/workflows/ci.yml:94` through `.github/workflows/ci.yml:126` pack `SafeIR.slnx`, run package metadata checks, and upload `artifacts/packages/*.nupkg` from the same token-bearing job.
- Existing release findings cover unexpected NuGet payloads, OS-specific artifact canonicality, mutable action tags, and unsigned/unattested artifacts; none require least-privilege workflow token scopes for the package-producing job.

## Risk

Release package jobs should be reproducible from reviewed workflow source, not from mutable repository-level token defaults. If the repository or organization default grants write scopes now or is changed later, every package-producing step and action in this job can receive broader authority than needed for build/test/pack/upload. A compromised dependency restore, build target, test, package script, or action in a release/tag run could use the ambient token to mutate repository contents, tags, releases, checks, or other GitHub state while the workflow still produces valid-looking package artifacts.

This is distinct from package signing/attestation: signatures prove artifact provenance after packaging, while least-privilege `permissions` limits what a compromised package-producing job can do during the run.

## Suggested test

Add a workflow lint check that parses `.github/workflows/*.yml` and fails package-producing jobs that omit explicit `permissions`. Include a negative fixture with no `permissions:` and a positive fixture where the build/test/pack job is limited to `contents: read` plus any explicitly justified read-only scopes.

## Expected behavior

The package-producing job should declare the minimum token permissions it needs, normally `contents: read` for checkout/build/test/pack and artifact upload. Any future publish, provenance, or attestation job should be split or scoped separately with only the additional permissions it requires, such as `id-token: write` and `attestations: write` for an attestation step.

## Suggested fix direction

Add explicit workflow or job-level permissions to `.github/workflows/ci.yml`, for example `permissions: { contents: read }` on `build-test-pack`, and add a small workflow-permissions lint so package-producing jobs cannot silently fall back to repository defaults. If a later release job needs write privileges, isolate it from build/test/package production and document the exact scopes.

## Deduplication key

`security/release-workflow/github-token/ambient-default-permissions`
