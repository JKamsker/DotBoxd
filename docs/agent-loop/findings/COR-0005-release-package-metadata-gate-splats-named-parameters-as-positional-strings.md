---
id: COR-0005
area: correctness
status: verified
priority: high
title: Release package metadata gate splats named parameters as positional strings
dedup_key: ci/release-gate/package-metadata/powershell-array-splatting
created_at: 2026-06-12T20:39:34.8803084+00:00
created_by: ci-release-readiness-reviewer
created_commit: 
updated_at: 2026-06-12T20:53:47.5610768+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:42:09.9519353+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:43:12.0529477+00:00
fixed_commit: working-tree
verified_by: independent-verifier
verified_at: 2026-06-12T20:53:47.5610768+00:00
verified_commit: 
duplicate_of: 
---

# COR-0005: Release package metadata gate splats named parameters as positional strings

## Claim
The release/tag-only stable package metadata step in `.github/workflows/ci.yml` builds an array of strings and invokes `./scripts/check-package-metadata.ps1 @args`. For PowerShell script/function calls, array splatting passes those strings positionally; it does not bind `"-PackageDirectory"` as a named parameter. As a result, the release/tag-only gate calls the metadata checker with `PackageDirectory` set to the literal string `-PackageDirectory` and fails before validating packages.

## Why this matters
This step only runs on `release/**` branches and tags, so normal pull request CI does not exercise it. A release branch or tag can pass the regular package metadata check and then fail the stable release gate because the workflow invokes the checker incorrectly.

## Evidence
The workflow step constructs an array and splats it:

```text
.github/workflows/ci.yml:106 "-PackageDirectory", "artifacts/packages"
.github/workflows/ci.yml:116 ./scripts/check-package-metadata.ps1 @args
```

Local reproduction using the same array-splatting pattern after a clean pack directory and successful non-release metadata check:

```powershell
$env:GITHUB_REF='refs/tags/v0.1.0'
$env:GITHUB_REF_NAME='v0.1.0'
$args = @('-PackageDirectory','artifacts/packages','-AllowedPrereleasePackageIds','DotBoxd.Pushdown.Services','-ExpectedVersion',$env:GITHUB_REF_NAME)
.\scripts\check-package-metadata.ps1 @args
```

Failure:

```text
Package directory does not exist: C:\Users\Jonas\repos\private\JKamsker\Safe-IR.worktrees\workflow-work\-PackageDirectory
```

The same failure also occurs with a non-reserved array variable, confirming the issue is array splatting to a PowerShell script rather than the `v` prefix normalization.

## Suggested test or benchmark
Add a CI-equivalent script test or runbook validation that exercises the release/tag-only metadata invocation exactly as the workflow does. At minimum, validate locally with:

```powershell
Remove-Item artifacts\packages\*.nupkg -Force
 dotnet pack DotBoxd.Kernels.slnx --configuration Release --no-build --output artifacts/packages
.\scripts\check-package-metadata.ps1 -PackageDirectory artifacts/packages -AllowedPrereleasePackageIds DotBoxd.Pushdown.Services -ExpectedVersion v0.1.0
```

Then validate the workflow snippet no longer uses array splatting for named parameters.

## Suggested fix direction
Use direct named-parameter invocation or a hashtable splat. For example, build `$metadataArgs = @{ PackageDirectory = 'artifacts/packages'; AllowedPrereleasePackageIds = @('DotBoxd.Pushdown.Services') }`, add `ExpectedVersion` when needed, and invoke `./scripts/check-package-metadata.ps1 @metadataArgs`.

## Scope boundaries
Do not weaken stable package validation and do not change package version policy. This is only about making the release/tag-only gate invoke the existing checker correctly.

## Deduplication key
`ci/release-gate/package-metadata/powershell-array-splatting`

## Verification checklist
- [ ] Reproduction or test exists where practical.
- [ ] Fix addresses root cause.
- [ ] Relevant tests pass.
- [ ] Perf/allocation evidence exists where practical.
- [ ] No unrelated behavior changed.
