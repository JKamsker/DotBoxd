---
id: COR-0053
area: correctness
status: fixed_pending_verification
priority: medium
title: Worker failure errors accept unsafe SafeMessage payloads
dedup_key: correctness/hosting/worker-result/unsafe-error-message
created_at: 2026-06-12T23:30:29.5159341+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T00:36:00.1092181+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:33:31.3647487+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:36:00.1092181+00:00
fixed_commit: pending
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0053: Worker failure errors accept unsafe SafeMessage payloads

## Claim

Host-side worker result validation accepts failed worker results with arbitrary `SandboxError.SafeMessage` and `DiagnosticId` payloads. A buggy or compromised worker can return a defined error code but unsafe top-level error text, and the host will publish that `SandboxExecutionResult` as trusted output.

## Evidence

`src/DotBoxd.Hosting/SandboxWorkerExecutor.cs` validates failed worker payloads in `WorkerPayloadMatches(...)` with only `result.Value is null`, `result.Error is not null`, and `Enum.IsDefined(result.Error.Code)`. It does not validate `result.Error.SafeMessage` or `result.Error.DiagnosticId` for null/empty values, control characters, excessive length, or secret-shaped text.

`src/DotBoxd.Kernels/Sandbox/SandboxError.cs` defines `SandboxError` as a public record with `SandboxErrorCode Code`, `string SafeMessage`, and optional `string? DiagnosticId`; there is no constructor guard on those text fields. `src/DotBoxd.Kernels/Model/Diagnostics.cs` also forwards `error.SafeMessage` to `SandboxRuntimeException.Message`, reinforcing that this field is expected to be safe for host-facing diagnostics.

The audit validator is stricter for audit envelopes: `src/DotBoxd.Hosting/WorkerAuditValidator.cs` rejects audit `Kind`, `ResourceId`, `Message`, field keys, and field values containing control characters. That protection does not apply to the top-level `result.Error` returned to callers from `SandboxWorkerExecutor.ExecuteAsync(...)`.

This is distinct from the existing worker error-code findings: `COR-0006` and `COR-0022` cover undefined enum values, while this issue covers trusted top-level error text attached to a defined code.

## Impact

Worker-process isolation is a trust/serialization boundary. Accepting arbitrary `SafeMessage` and `DiagnosticId` values lets out-of-process data become host-trusted public error output even when the audit summary itself is sanitized. Callers, logs, UIs, exception bridges, or telemetry pipelines that treat `SafeMessage` as safe can receive control-character/log-injection payloads or secret-shaped diagnostic text from a malformed worker instead of a host-side `HostFailure` for an invalid worker envelope.

## Suggested tests

Extend worker result hardening tests with failed worker results whose `Error.Code` is a valid `SandboxErrorCode`, the run summary otherwise matches, but `Error.SafeMessage` contains control characters or a secret-shaped string, and another case where `DiagnosticId` contains control characters. The host should reject the worker result and return a `WorkerIsolationFailed` result with `SandboxErrorCode.HostFailure`.

## Expected behavior

Worker result validation should reject or sanitize unsafe top-level `SandboxError` text before returning a worker result. Prefer rejecting malformed worker envelopes so the caller receives a host-generated `HostFailure` and accepted worker errors obey the same safe-text contract as accepted worker audit events.

## Deduplication key

`correctness/hosting/worker-result/unsafe-error-message`
