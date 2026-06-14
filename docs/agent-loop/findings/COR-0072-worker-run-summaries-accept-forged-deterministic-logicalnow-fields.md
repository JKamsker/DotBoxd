---
id: COR-0072
area: correctness
status: open
priority: medium
title: Worker run summaries accept forged deterministic logicalNow fields
dedup_key: correctness/hosting/worker-run-summary/logical-now-forgery
created_at: 2026-06-13T06:56:19.4926438+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-13T06:56:19.4926438+00:00
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

# COR-0072: Worker run summaries accept forged deterministic logicalNow fields

## Summary

Worker run-summary validation allows deterministic workers to add a `logicalNow` field with arbitrary safe text, but `WorkerRunSummaryMatches(...)` never compares that value with `plan.Policy.LogicalNow` or rejects it as an unexpected field.

## Evidence

- `src/DotBoxd.Hosting/WorkerAuditValidator.cs:113` to `src/DotBoxd.Hosting/WorkerAuditValidator.cs:116` whitelists `logicalNow` whenever `plan.Policy.Deterministic` is true.
- `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:238` to `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:258` compares required identity and resource fields, but not `logicalNow`.
- `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:263` checks budget ceiling fields, and `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:268` to `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:288` checks compiled envelope fields, but still never validates `logicalNow`.
- `src/DotBoxd.Kernels/Model/RunSummaryAuditFields.cs:16` to `src/DotBoxd.Kernels/Model/RunSummaryAuditFields.cs:56` does not emit a trusted `logicalNow` field for host-created summaries, so the worker boundary is accepting a worker-supplied field that is not regenerated from host state.

## Impact

A worker can publish trusted deterministic-run audit evidence that claims a different logical clock than the policy actually enforced. Consumers using run-summary fields for replay, deterministic audit correlation, or compliance checks can be misled even though timestamps and plan hashes otherwise pass validation.

## Suggested test

Add a worker hardening test with a deterministic policy and `LogicalNow = 2026-06-12T12:00:00Z`. Have the worker return an otherwise valid result and run summary, but add `logicalNow = 1999-01-01T00:00:00Z` or another malformed value. The host should reject the envelope, or the host should rewrite/remove the field before publishing the result.

## Suggested fix

Either remove `logicalNow` from `WorkerAuditValidator.FieldNameAllowed(...)`, or make `WorkerRunSummaryMatches(...)` require it to equal the invariant representation of `plan.Policy.LogicalNow` when present. Prefer generating trusted run-summary fields on the host from validated result data instead of trusting optional worker-supplied identity fields.

## Deduplication

Existing worker run-summary findings cover forged budget ceilings, cache telemetry, and policy identity fields. This finding is specific to the deterministic logical clock field.
