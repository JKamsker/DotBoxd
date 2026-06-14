---
id: COR-0003
area: correctness
status: fixed_pending_verification
priority: medium
title: Package metadata check fails on stale packages left in output directory
dedup_key: release-packaging/package-metadata/stale-output-directory
created_at: 2026-06-12T20:36:53.8569218+00:00
created_by: ci-release-readiness-reviewer
created_commit: 
updated_at: 2026-06-12T20:48:26.8274149+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:46:55.0847634+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:48:26.8274149+00:00
fixed_commit: working-tree
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0003: Package metadata check fails on stale packages left in output directory

## Claim
The package metadata gate scans every `.nupkg` already present in `artifacts/packages`, while the documented/CI pack command writes new packages into that same directory without cleaning it first. A stale package in the output directory can make an otherwise current pack fail metadata validation.

## Why this matters
Release validation should be reproducible in a reused local workspace and in release automation that reuses artifacts between attempts. Without cleaning or isolating the output directory, the metadata gate can fail on packages that were not produced by the current pack operation.

## Evidence
After `dotnet pack DotBoxd.Kernels.slnx --configuration Release --no-build --output artifacts/packages`, the directory contained both current and stale IPC packages:

```text
DotBoxd.Pushdown.Services.0.1.0-dotboxd-ci.20.nupkg
DotBoxd.Pushdown.Services.0.1.0-dotboxd-ci.30.nupkg
```

Running the metadata gate failed on the stale package:

```powershell
.\scripts\check-package-metadata.ps1 -PackageDirectory artifacts/packages -AllowPrereleaseVersions
```

Failure:

```text
Package DotBoxd.Pushdown.Services.0.1.0-dotboxd-ci.20.nupkg repository commit '342f6998670faf1f28ccf5d169386246de4ec173' does not match current commit '37d0549f7b24c6a863b9851b6722ff324edc6b58'.
```

## Suggested test or benchmark
Create a stale `.nupkg` in the package output directory, run the documented pack plus metadata check sequence, and verify the release path either cleans the directory first or validates only packages produced by the current pack.

Suggested validation:

```powershell
Remove-Item artifacts\packages\*.nupkg -Force
 dotnet pack DotBoxd.Kernels.slnx --configuration Release --no-build --output artifacts/packages
.\scripts\check-package-metadata.ps1 -PackageDirectory artifacts/packages -AllowPrereleaseVersions
.\scripts\check-package-metadata.ps1 -PackageDirectory artifacts/packages -AllowedPrereleasePackageIds DotBoxd.Pushdown.Services -ExpectedVersion 0.1.0
```

## Suggested fix direction
Make the release packaging path clean or unique before packing. For example, have CI and README guidance remove `artifacts/packages/*.nupkg` before `dotnet pack`, or change the metadata checker to accept an explicit expected package set produced by the current invocation.

## Scope boundaries
Do not weaken package metadata validation. The fix should remove stale-input sensitivity without allowing outdated packages through the release gate.

## Deduplication key
`release-packaging/package-metadata/stale-output-directory`

## Verification checklist
- [ ] Reproduction or test exists where practical.
- [ ] Fix addresses root cause.
- [ ] Relevant tests pass.
- [ ] Perf/allocation evidence exists where practical.
- [ ] No unrelated behavior changed.
