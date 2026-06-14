---
id: COR-0071
area: correctness
status: open
priority: medium
title: WorkerExecution audit events can contradict accepted worker results
dedup_key: correctness/hosting/worker-audit/worker-execution-result-mismatch
created_at: 2026-06-13T06:56:18.2633970+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-13T06:56:18.2633970+00:00
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

# COR-0071: WorkerExecution audit events can contradict accepted worker results

## Summary

Worker-isolated results can include a non-summary `WorkerExecution` audit event whose success/error state contradicts the accepted top-level result. The host validates the event shape, but it never reconciles `WorkerExecution.Success` or `WorkerExecution.ErrorCode` with `SandboxExecutionResult.Succeeded`.

## Evidence

- `src/DotBoxd.Hosting/WorkerAuditValidator.cs:61` accepts `WorkerExecution` events through `ModuleAuditMatches(...)`.
- `src/DotBoxd.Hosting/WorkerAuditValidator.cs:118` to `src/DotBoxd.Hosting/WorkerAuditValidator.cs:123` checks only module-shaped fields: no binding/capability/effect fields and `ResourceId == module:{plan.ModuleHash}`.
- `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:211` to `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:217` applies that schema to each event.
- `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:219` to `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:230` reconciles only the single `RunSummary` with the result, not the accepted `WorkerExecution` events.
- `tests/DotBoxd.Kernels.Tests/Misc08/WorkerAuditValidationTests.cs` covers undefined error codes, forged binding/policy/cache events, unknown kinds, and timestamps, but it does not cover a `WorkerExecution` event with a defined failure code attached to an otherwise successful result.

## Impact

A buggy or compromised worker can return `Succeeded = true` with a valid successful run summary, while also including `WorkerExecution` with `Success = false` and `ErrorCode = SandboxErrorCode.HostFailure`. The host accepts and publishes both pieces of trusted audit evidence. Downstream audit exporters, retry automation, or incident tooling can observe a successful result and a host-trusted worker failure event for the same run.

## Suggested test

Extend `WorkerAuditValidationTests` with a worker that returns a successful result and valid successful run summary, plus a `WorkerExecution` event for the same run/module with `Success = false` and a defined error code. The host should reject the worker envelope with `SandboxErrorCode.HostFailure`. Add the inverse case for a failed top-level result with a successful `WorkerExecution` event.

## Suggested fix

In worker audit validation, require `WorkerExecution` events to agree with the top-level result success and error state, or generate the `WorkerExecution` event on the host after validating the worker response. If multiple `WorkerExecution` events are not meaningful, require exactly one.

## Deduplication

This is narrower than the earlier non-summary forgery findings: the current validator rejects forged binding/policy/cache events, but still accepts a schema-valid `WorkerExecution` event whose result state contradicts the accepted worker result.
