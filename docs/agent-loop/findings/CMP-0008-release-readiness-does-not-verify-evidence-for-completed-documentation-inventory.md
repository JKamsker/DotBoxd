---
id: CMP-0008
area: completeness
status: open
priority: medium
title: Release readiness does not verify evidence for completed documentation inventory items
dedup_key: cmp-release-readiness-documentation-inventory-evidence-coverage
created_at: 2026-06-12T22:03:21.1281819+00:00
created_by: Codex completeness auditor
created_commit: 
updated_at: 2026-06-12T22:03:21.1281819+00:00
claimed_by: 
claimed_at: 
claim_branch: 
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# CMP-0008: Release readiness does not verify evidence for completed documentation inventory items

## Claim

The release readiness checklist marks documentation inventory items complete, but the release readiness script does not verify evidence for those completed documentation items. As a result, documentation coverage can drift or files can disappear without `check-release-readiness.ps1` detecting that the completed checklist entries are no longer supported.

## Why this matters

Completed checklist entries are release evidence. Even if documentation is inventory rather than a blocking release gate, marking every documentation item `[x]` should be backed by durable evidence or an explicit non-gated report; otherwise the checklist overstates release completeness.

## Evidence

- `docs/Specs/Initial/safe-ir-sandbox-spec/checklists/release-readiness.md` has the `Documentation` section marked `release-gate: inventory`, and all entries in that section are checked complete.
- `scripts/check-release-readiness.ps1` builds `$releaseEvidence` only for required MVP and compiled-mode release checklist entries, then reports missing release evidence only for `$_ .Required -and $_.Complete` items in `release-readiness.md`.
- The same script validates security review sections through `$securitySectionEvidence`, but there is no equivalent evidence map for completed documentation inventory items such as user-facing language docs, capability catalog, error code reference, debugging guide, or operational runbook.
- The existing release-readiness findings cover stale required evidence paths and package metadata gates; this gap is specifically about completed inventory documentation items having no evidence coverage.

## Suggested test or benchmark

Add a Pester/script-level test or self-check that fails when a completed release-readiness checklist item, including completed inventory documentation items, has no evidence mapping or evidence link. A minimal acceptance case can temporarily rename one documented evidence file and assert `check-release-readiness.ps1` reports the missing documentation evidence.

## Suggested fix direction

Extend `check-release-readiness.ps1` with a documentation evidence map or require inline evidence links for completed documentation checklist items. Keep `-RequireComplete` semantics unchanged if inventory items should remain non-blocking, but make the normal inventory report verify that checked documentation items still point at existing docs with expected headings/patterns.

## Scope boundaries

Do not change production code. Do not make open inventory items block stable release unless release policy is intentionally changed.

## Deduplication key

`cmp-release-readiness-documentation-inventory-evidence-coverage`

## Verification checklist

- [ ] Completed documentation inventory items have evidence checks or explicit evidence links.
- [ ] `check-release-readiness.ps1` detects missing/stale documentation evidence.
- [ ] Existing required release gate behavior remains intact.
- [ ] No production source code changes are needed.
