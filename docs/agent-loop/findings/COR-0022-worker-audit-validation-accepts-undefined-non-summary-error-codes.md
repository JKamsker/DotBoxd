---
id: COR-0022
area: correctness
status: verified
priority: medium
title: Worker audit validation accepts undefined non-summary error codes
dedup_key: correctness/hosting/worker-audit/undefined-non-summary-error-code
created_at: 2026-06-12T22:15:24.3439843+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T23:07:10.7955869+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:52:41.0070303+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:03:02.6840953+00:00
fixed_commit: 
verified_by: independent-verifier
verified_at: 2026-06-12T23:07:10.7955869+00:00
verified_commit: 
duplicate_of: 
---

# COR-0022: Worker audit validation accepts undefined non-summary error codes

## Claim

Worker-process result validation accepts worker-supplied audit events whose `ErrorCode` is an undefined `SandboxErrorCode` value, as long as the overall result payload and the single `RunSummary` are otherwise valid.

## Evidence

`src/SafeIR.Hosting/SandboxWorkerExecutor.cs` validates worker results before returning them from `ExecuteAsync`. The hardened payload path now rejects undefined `result.Error.Code`, and `WorkerAuditMatches` validates the single `RunSummary` against the result error. However, `WorkerAuditMatches` only checks that all audit events share the same run id, that there is exactly one `RunSummary`, and that the summary fields match the result. It does not validate `ErrorCode` on the rest of `result.AuditEvents` before returning the accepted worker result with resequenced audit events.

A worker can therefore return a successful result with a normal successful `RunSummary` plus an extra event such as:

```csharp
new SandboxAuditEvent(runId, "BindingCall", DateTimeOffset.UtcNow, false,
    BindingId: "x", ErrorCode: (SandboxErrorCode)123456)
```

If the summary fields match the result and resource usage, `ValidateWorkerResult` accepts the envelope and `ExecuteAsync` publishes the malformed audit event to callers and audit observers. `src/SafeIR.Core/Sandbox/SandboxError.cs` defines `SandboxErrorCode` as a plain enum, so constructing such an event is possible. Existing `tests/SafeIR.Tests/Misc08/WorkerResultHardeningTests.cs` covers undefined failure result codes and malformed summaries, but it does not cover undefined non-summary audit event codes.

## Risk

The host-side worker validator is the boundary that turns out-of-process audit output back into trusted public `SandboxExecutionResult` evidence. Letting malformed enum values through non-summary audit events reintroduces an impossible public state for telemetry, audit exporters, and policy automation even when the top-level result is valid. Consumers that switch over documented `SandboxErrorCode` values can misclassify or fail on these events.

## Suggested test

Extend `WorkerResultHardeningTests` with a worker that returns a valid successful result and valid successful `RunSummary`, but also appends a non-summary audit event with `ErrorCode = (SandboxErrorCode)123456`. The host should reject the worker result with `SandboxErrorCode.HostFailure` and emit `WorkerIsolationFailed`, rather than publishing the malformed event. Add the same coverage for a failed non-summary event when the top-level failure code and summary code are valid.

## Expected behavior

Worker audit validation should reject every audit event whose nullable `ErrorCode` is present but not `Enum.IsDefined`, not just the top-level result error and `RunSummary` error code.

## Suggested fix direction

In `SandboxWorkerExecutor.WorkerAuditMatches`, validate all `result.AuditEvents` before accepting the worker result: if `event.ErrorCode is { } code && !Enum.IsDefined(code)`, return false. Keep the existing stricter `RunSummary` matching and continue converting malformed worker envelopes to `WorkerIsolationFailedResult(... HostFailure ...)`.

## Deduplication key

`correctness/hosting/worker-audit/undefined-non-summary-error-code`
