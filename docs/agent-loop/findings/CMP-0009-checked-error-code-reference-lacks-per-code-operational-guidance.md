---
id: CMP-0009
area: completeness
status: open
priority: medium
title: Checked error code reference lacks per-code operational guidance
dedup_key: docs/error-code-reference/checked-complete-without-per-code-reference
created_at: 2026-06-12T22:08:13.6123601+00:00
created_by: continuous-completeness-producer
created_commit: 
updated_at: 2026-06-12T22:08:13.6123601+00:00
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

# CMP-0009: Checked error code reference lacks per-code operational guidance

## Claim

The release checklist marks the error code reference complete, but the docs do not contain a maintained per-code reference that covers every `SandboxErrorCode` with meaning, common causes, retry/admin guidance, and audit expectations.

## Why this matters

Hosts need stable error-code guidance to decide whether to retry, show a tenant-safe message, escalate to admins, or treat a result as a policy/security event. A bare enum listing is not enough for operators or SDK consumers.

## Evidence

- `docs/Specs/Initial/dotboxd-sandbox-spec/checklists/release-readiness.md:65` marks `Error code reference` as complete.
- `src/DotBoxd.Kernels/Sandbox/SandboxError.cs:8` through `src/DotBoxd.Kernels/Sandbox/SandboxError.cs:21` define the current error taxonomy: `ValidationError`, `PolicyDenied`, `PermissionDenied`, `NotFound`, `InvalidInput`, `QuotaExceeded`, `Timeout`, `Cancelled`, `BindingFailure`, `VerifierFailure`, `CacheInvalid`, and `HostFailure`.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:557` through `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:570` repeat the enum, but do not describe caller behavior or audit expectations per code.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/07-bindings.md:330` through `docs/Specs/Initial/dotboxd-sandbox-spec/spec/07-bindings.md:336` map only binding-specific host conditions to a subset of codes.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/08-runtime-safe-apis.md:310` through `docs/Specs/Initial/dotboxd-sandbox-spec/spec/08-runtime-safe-apis.md:321` list a smaller safe-error set and omit several current public codes, including `ValidationError`, `PolicyDenied`, `BindingFailure`, `VerifierFailure`, and `CacheInvalid`.

## Suggested acceptance test

Add a docs smoke test that parses `SandboxErrorCode` and verifies a dedicated error-code reference contains exactly one entry for every enum value. Each entry should include safe user message guidance, likely causes, retryability, audit/event expectations, and admin escalation notes.

## Suggested fix direction

Create a maintained `error-codes.md` reference under the initial spec or operations docs and link it from the README/public API docs and release checklist. Generate or test the code list against `SandboxErrorCode` so new enum values cannot ship undocumented.

## Scope boundaries

Do not change error-code behavior here. This finding is only about user/operator documentation completeness for the existing taxonomy.

## Deduplication key

`docs/error-code-reference/checked-complete-without-per-code-reference`
