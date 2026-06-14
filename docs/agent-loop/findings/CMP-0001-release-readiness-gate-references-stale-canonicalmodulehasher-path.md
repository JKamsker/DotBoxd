---
id: CMP-0001
area: completeness
status: verified
priority: high
title: Release readiness gate references stale CanonicalModuleHasher path
dedup_key: release-readiness/checklist-evidence/canonical-module-hasher-path
created_at: 2026-06-12T20:36:52.4586644+00:00
created_by: ci-release-readiness-reviewer
created_commit: 
updated_at: 2026-06-12T20:52:17.8202128+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:38:40.9463469+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:39:15.9434957+00:00
fixed_commit: working-tree
verified_by: independent-verifier
verified_at: 2026-06-12T20:52:17.8202128+00:00
verified_commit: 
duplicate_of: 
---

# CMP-0001: Release readiness gate references stale CanonicalModuleHasher path

## Claim
`check-release-readiness.ps1` fails before release enforcement because a completed release checklist item points at a stale source path. The script expects canonical hash evidence at `src/DotBoxd.Kernels/CanonicalModuleHasher.cs`, but the implementation currently lives at `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs`.

## Why this matters
The normal CI step `Report release readiness checklist` and the release/tag-only `-RequireComplete` gate both execute this script. As written, release readiness cannot be reported or enforced even though the evidence appears to exist at the moved path.

## Evidence
Local reproduction after successful locked restore, Release build, tests, required security tests, docs smoke, CodeEnforcer, and spec manifest:

```powershell
.\scripts\check-release-readiness.ps1
```

Failure:

```text
Release checklist item 'Canonical hashing implemented.' is marked complete but evidence is missing: src/DotBoxd.Kernels/CanonicalModuleHasher.cs
```

Static evidence:

```text
scripts/check-release-readiness.ps1:82 Path = "src/DotBoxd.Kernels/CanonicalModuleHasher.cs"
src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:3 public static class CanonicalModuleHasher
src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:7 public static string Hash(SandboxModule module)
```

## Suggested test or benchmark
Run both release readiness modes after the fix:

```powershell
.\scripts\check-release-readiness.ps1
.\scripts\check-release-readiness.ps1 -RequireComplete
```

A regression test for the release-readiness script is also appropriate if the script has or gains Pester/CLI coverage.

## Suggested fix direction
Update the release evidence path in `scripts/check-release-readiness.ps1` from `src/DotBoxd.Kernels/CanonicalModuleHasher.cs` to `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs`, or make the evidence mapping resilient to the current source layout.

## Scope boundaries
Do not change canonical hashing behavior as part of this fix. This finding is only about the release checklist evidence gate using a stale path.

## Deduplication key
`release-readiness/checklist-evidence/canonical-module-hasher-path`

## Verification checklist
- [ ] Reproduction or test exists where practical.
- [ ] Fix addresses root cause.
- [ ] Relevant tests pass.
- [ ] Perf/allocation evidence exists where practical.
- [ ] No unrelated behavior changed.
