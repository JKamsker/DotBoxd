---
id: COR-0006
area: correctness
status: fixed_pending_verification
priority: medium
title: Worker result validation accepts undefined failure error codes
dedup_key: correctness/hosting/worker-result/undefined-error-code
created_at: 2026-06-12T21:00:37.0246004+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T21:06:20.3336419+00:00
claimed_by: implementer
claimed_at: 2026-06-12T21:04:29.3038430+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T21:06:20.3336419+00:00
fixed_commit: working-tree
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0006: Worker result validation accepts undefined failure error codes

## Claim

Worker-process result validation accepts failed worker results whose `SandboxError.Code` is not a defined `SandboxErrorCode` value, as long as the malformed code is echoed in the worker run summary.

## Evidence

`src/DotBoxd.Hosting/SandboxWorkerExecutor.cs` validates worker results before returning them from `ExecuteAsync`. `WorkerPayloadMatches` only requires failed results to have `Value is null` and `Error is not null`; it does not validate that `result.Error.Code` is a defined enum value. `WorkerAuditMatches` then accepts the failure when the single `RunSummary` has `ErrorCode == result.Error.Code`, so a compromised or buggy worker can return `(SandboxErrorCode)123456` consistently in both places and the host will publish that malformed error to callers.

`src/DotBoxd.Kernels/Sandbox/SandboxError.cs` defines `SandboxError` as a plain record over `SandboxErrorCode`; there is no constructor guard that rejects undefined enum values. Existing worker hardening tests cover wrong plan identity, invalid mode, wrong return type, missing error, malformed resource usage, and malformed summary fields, but there is no test for an undefined failure error code.

## Risk

Host-side worker validation is the boundary that turns out-of-process worker output back into trusted `SandboxExecutionResult` values. Accepting undefined error codes weakens that boundary: downstream policy, telemetry, retry, and audit handling can receive values outside the documented failure taxonomy instead of a host-side `HostFailure` for a malformed worker envelope.

## Suggested test

Extend `WorkerIsolationTests` or `WorkerResultHardeningTests` with a worker that returns `Succeeded = false`, `Value = null`, `Error = new SandboxError((SandboxErrorCode)123456, "bad code")`, and a `RunSummary` whose `ErrorCode` is the same undefined value. The host should reject the worker result with `SandboxErrorCode.HostFailure` and a `WorkerIsolationFailed` audit event, rather than returning the undefined code.

## Expected behavior

Worker result validation should reject failed results whose `Error.Code` is not `Enum.IsDefined`, and should similarly reject malformed audit summary error codes before publishing the worker result.

## Suggested fix direction

In `SandboxWorkerExecutor.WorkerPayloadMatches` or `ValidateWorkerResult`, require `Enum.IsDefined(result.Error.Code)` for failed results. In `WorkerAuditMatches`, ensure a failed summary has a defined `ErrorCode` matching the defined result error. Keep converting malformed worker envelopes to `WorkerIsolationFailedResult(... HostFailure ...)`.

## Deduplication key

`correctness/hosting/worker-result/undefined-error-code`
