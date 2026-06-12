---
id: CMP-0017
area: completeness
status: open
priority: medium
title: Audit observer surface lacks a runnable user-facing example
dedup_key: completeness/audit-observer/user-facing-example/missing-smoke
created_at: 2026-06-12T23:08:01.0434464+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T23:08:01.0434464+00:00
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

# CMP-0017: Audit observer surface lacks a runnable user-facing example

## Claim

`SandboxHostBuilder.ForwardAuditEventsTo(...)` is a public host API for operational audit streaming, but the runnable docs/examples do not show consumers how to wire an observer, verify event ordering, or prove observer failures are isolated from sandbox results.

## Evidence

- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:13` includes `builder.ForwardAuditEventsTo(audit => auditSink.Write(audit))` in the high-level public API sample, and `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:91` lists `ForwardAuditEventsTo(Action<SandboxAuditEvent> observer)` as part of `SandboxHostBuilder`.
- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:98` through `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:99` documents the important behavior: observer failures must not change the returned result or prevent later observers from receiving events.
- The implementation exists at `src/SafeIR.Hosting/Execution/SandboxHostBuilder.cs:82` through `src/SafeIR.Hosting/Execution/SandboxHostBuilder.cs:84`, and `src/SafeIR.Hosting/Execution/SandboxHost.cs:136` through `src/SafeIR.Hosting/Execution/SandboxHost.cs:150` publishes result audit events to observers.
- Test coverage exists in `tests/SafeIR.Tests/Misc01/AuditObserverTests.cs:9`, `:33`, `:57`, and `:82` for success, fail-closed forwarding, and throwing observers, so this is not a missing implementation finding.
- `scripts/check-docs-smoke.ps1:122` through `scripts/check-docs-smoke.ps1:216` only runs the addendum, local plugin, and IPC examples after checking documented command paths; none of those docs/examples contain `ForwardAuditEventsTo`.
- `scripts/check-release-readiness.ps1:119` through `scripts/check-release-readiness.ps1:124` only checks that basic audit implementation evidence contains `SandboxAuditEvent` and `IAuditSink`; it does not prove the public host observer workflow from a user-facing sample.
- A refreshed duplicate search found existing audit findings for correctness/perf behavior, including `COR-0014`, `COR-0023`, `COR-0027`, `PAL-0021`, `PAL-0024`, and `PAL-0034`, but no completeness/API finding for a missing runnable observer example.

## Impact

Audit observers are the package-facing integration point for operational telemetry, billing, incident review, and compliance export. Without a runnable consumer example or docs-smoke check, a release can ship while the documented observer contract drifts from the package surface, or while consumers have to infer safety-critical behavior from tests rather than public guidance.

## Better target

Add a small public docs/example path that creates a host with `ForwardAuditEventsTo`, executes a minimal module, asserts or prints that observed events match `SandboxExecutionResult.AuditEvents`, and demonstrates that a throwing observer does not change the result or block a later observer. Link it from `README.md` or the public API docs.

## Acceptance test idea

Extend `scripts/check-docs-smoke.ps1` to run the new audit-observer example so release validation proves the user-facing observer workflow, not only the internal audit model and unit tests.

## Scope boundaries

This does not change audit event immutability, redaction, worker audit validation, or observer dispatch allocation behavior. Those are covered by existing correctness/perf findings; this finding is only about the missing user-facing surface and release docs-smoke coverage for the public observer API.
