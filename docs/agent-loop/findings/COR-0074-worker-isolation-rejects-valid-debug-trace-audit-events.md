---
id: COR-0074
area: correctness
status: open
priority: medium
title: Worker isolation rejects valid debug trace audit events
dedup_key: correctness/hosting/worker-debug-trace/valid-events-rejected
created_at: 2026-06-13T07:01:06.0082169+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-13T07:01:06.0082169+00:00
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

# COR-0074: Worker isolation rejects valid debug trace audit events

## Summary

Worker-process execution can reject otherwise valid debug-traced runs. The host forwards `EnableDebugTrace` into the worker, but the worker audit validator only accepts module-shaped `DebugTrace` events with no fields, binding id, capability id, or effect. The interpreter emits `DebugTrace` events with structured fields, and binding traces also carry binding/capability/effect metadata, so a valid worker result can be converted into `WorkerIsolationFailed` just because tracing was enabled.

## Evidence

- `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs` creates `workerOptions = options with { Isolation = SandboxIsolation.InProcess }`, preserving `EnableDebugTrace` when it asks the worker to execute the plan.
- `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:65` and `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:30` call `InterpreterTrace.Write(...)` during interpreted execution when debug tracing is enabled.
- `src/DotBoxd.Kernels.Interpreter/Internal/InterpreterTrace.cs:23` writes `Kind = "DebugTrace"` events with a message and `Fields: DebugFields(...)`.
- `src/DotBoxd.Kernels.Interpreter/Internal/InterpreterTrace.cs:44` writes binding debug traces with `BindingId`, `CapabilityId`, `Effect`, a message, and structured fields.
- `src/DotBoxd.Hosting/WorkerAuditValidator.cs:65` accepts worker `DebugTrace` events only through `ModuleAuditMatches(...)`.
- `src/DotBoxd.Hosting/WorkerAuditValidator.cs:118` to `src/DotBoxd.Hosting/WorkerAuditValidator.cs:123` requires module-shaped events: no binding id, no capability id, `Effect == SandboxEffect.None`, `Fields is null`, and `ResourceId == module:{plan.ModuleHash}`.
- `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:211` to `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:217` rejects the entire worker envelope when any returned audit event fails that validator.
- The worker hardening tests in `tests/DotBoxd.Kernels.Tests/Misc08/WorkerAuditValidationTests.cs` and `tests/DotBoxd.Kernels.Tests/Misc08/WorkerResultHardeningTests.cs` cover forged audit shapes and malformed summaries, but there is no worker-process test that executes with `EnableDebugTrace = true` and expects valid trace events to survive.

## Impact

A caller requesting worker isolation and debug tracing can receive a host-side `HostFailure`/`WorkerIsolationFailed` result for a run that completed successfully in the worker. This makes debug tracing unusable for worker-isolated interpreted fallback paths and can mask the actual sandbox result while publishing a misleading worker-isolation failure.

## Suggested test

Add a worker-isolation test that executes a simple interpreted entrypoint with `new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess, EnableDebugTrace = true }`. Use the normal hardened worker test client or a worker that delegates to the interpreter and returns its real audit events. The host should return the successful sandbox result and include the expected `DebugTrace` events instead of rejecting the envelope.

Add a binding trace variant using a binding such as `math.abs` or `log.info`, so the accepted debug trace path covers events with binding/capability/effect metadata and structured fields.

## Fix direction

Align worker `DebugTrace` validation with the trace shapes produced by `InterpreterTrace`, including bounded/safe structured fields and the binding trace metadata that the interpreter legitimately emits. Alternatively, strip or host-regenerate debug trace events at the worker boundary, but do not preserve `EnableDebugTrace` into the worker while rejecting the worker's valid trace output.

## Deduplication

Existing worker audit findings cover forged non-summary events, contradictory `WorkerExecution` state, forged run-summary identity/cache/logical-clock fields, and malformed result payloads. This finding is specific to valid debug trace audit events being rejected because the worker validator's accepted `DebugTrace` schema does not match the interpreter's emitted schema.
