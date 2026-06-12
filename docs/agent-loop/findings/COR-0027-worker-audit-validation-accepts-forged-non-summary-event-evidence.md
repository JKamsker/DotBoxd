---
id: COR-0027
area: correctness
status: fixed_pending_verification
priority: high
title: Worker audit validation accepts forged non-summary event evidence
dedup_key: security/hosting/worker-audit/non-summary-event-forgery
created_at: 2026-06-12T22:23:28.5564357+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T23:11:55.9862807+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:09:05.8137045+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:11:55.9862807+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0027: Worker audit validation accepts forged non-summary event evidence

## Claim

Worker-process result validation accepts arbitrary non-summary audit event identities and field payloads from the worker, then resequences and publishes them as trusted host audit evidence.

## Evidence

`src/SafeIR.Hosting/SandboxWorkerExecutor.cs` validates worker results in `ValidateWorkerResult`. The audit path in `WorkerAuditMatches` requires at least one audit event, checks that all events share the same run id, requires exactly one `RunSummary`, and validates that summary against the result and resource usage.

The same method does not validate the rest of `result.AuditEvents`: it does not reject unknown `Kind` values, forged `BindingId` values, capabilities or effects not reachable from `plan.BindingReferences`, missing binding audit fields, malformed `resourceKind` or timing fields, arbitrary `Message`/`ResourceId` text, or success `BindingCall` records for bindings the verified entrypoint never executed.

After that shallow envelope check, `ExecuteAsync` returns the worker result with `AuditEvents = result.AuditEvents.ToSequencedArray()`. `SandboxHost.ExecuteAsync` then publishes every accepted event to audit observers. Existing `tests/SafeIR.Tests/Misc08/WorkerResultHardeningTests.cs` cover malformed run summaries and top-level worker result mismatches, and COR-0022 covers undefined non-summary error codes, but I did not find coverage that rejects forged non-summary event kinds, binding identities, capabilities, effects, or field dictionaries.

A compromised or buggy worker can therefore return a valid successful pure result plus a synthetic event such as a successful `BindingCall` for `file.writeText` or `net.http.get`, with arbitrary fields and resource text. As long as the single run summary matches, the host accepts and republishes the forged event.

## Risk

Worker isolation is a serialization/trust boundary: the host-side validator turns out-of-process data back into public `SandboxExecutionResult` and audit-observer input. Accepting arbitrary worker-supplied non-summary audit events undermines audit integrity. Downstream policy automation, billing, incident review, or compliance export can observe host-trusted evidence for resource access that the verified entrypoint did not perform, or attacker-controlled diagnostic fields that never passed the in-process binding audit checks.

## Suggested test

Extend `WorkerResultHardeningTests` with a worker that returns a valid successful pure result and a valid `RunSummary`, plus an extra successful `BindingCall` whose `BindingId` is `file.writeText`, `CapabilityId` is `file.write`, `Effect` is `FileWrite`, and fields/resource values do not correspond to the pure plan. The host should reject the worker result with `SandboxErrorCode.HostFailure` and emit `WorkerIsolationFailed` instead of publishing the forged event. Add a second case for an unknown event kind or missing binding audit fields.

## Expected behavior

Worker audit validation should accept only a strict schema for audit events that can be produced by the validated plan and execution mode. Non-summary events should have known kinds, defined error codes, sane timestamps/fields, matching module and policy hashes, and binding/capability/effect identities that are permitted by the entrypoint's verified binding references.

## Suggested fix direction

Add full non-summary audit validation in `SandboxWorkerExecutor.WorkerAuditMatches`: validate known event kinds, reject unexpected fields and undefined enum values, require binding audit fields for binding events, verify module/policy hashes, and cross-check binding events against `plan.BindingReferences` and the host binding registry. Consider dropping worker-supplied non-summary events entirely unless they can be proven to match the validated execution plan.

## Deduplication key

security/hosting/worker-audit/non-summary-event-forgery
