---
id: COR-0055
area: correctness
status: fixed_pending_verification
priority: medium
title: Binding failure audit enforcement accepts mismatched error codes
dedup_key: correctness/runtime/binding-failure-audit/error-code-mismatch
created_at: 2026-06-12T23:32:38.9038910+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T00:32:58.5285855+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:29:09.2595791+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:32:58.5285855+00:00
fixed_commit: pending
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0055: Binding failure audit enforcement accepts mismatched error codes

## Claim

Required binding failure audit enforcement accepts any failed binding audit after the checkpoint, even when that audit reports a different error code from the error that actually caused the binding call to fail.

## Evidence

`src/DotBoxd.Kernels/Sandbox/SandboxContext.cs` passes the actual failure code into `EnsureRequiredBindingFailureAudit(BindingDescriptor descriptor, long checkpoint, SandboxErrorCode errorCode)`. The interpreter and compiled dispatchers call this method from failure paths with `ex.Error.Code`, timeout, cancellation, or generic binding-failure codes.

Inside `EnsureRequiredBindingFailureAudit(...)`, the supplied `errorCode` is only used when synthesizing a fallback audit. The method first calls `Audit.HasBindingAuditSince(descriptor, checkpoint, success: false, RunId, ModuleHash, PolicyHash)`, which has no parameter for the expected error code.

`src/DotBoxd.Kernels/Bindings/Audit.cs` implements `InMemoryAuditSink.HasBindingAuditSince(...)` by checking binding id, capability, effect, resource id, required fields, and for failures only `(success || e.ErrorCode is not null)`. It does not require `e.ErrorCode == errorCode` for the failure being handled.

A binding can therefore write a structurally valid failed `BindingCall` audit with `ErrorCode = SandboxErrorCode.NotFound` and then throw `new SandboxRuntimeException(new SandboxError(SandboxErrorCode.QuotaExceeded, ...))`. The runtime sees a failed audit since the checkpoint and does not synthesize the correct `QuotaExceeded` failure audit, so the returned `SandboxExecutionResult.Error.Code` and the binding audit evidence disagree.

This is distinct from `COR-0043`, which covers the timestamp used when a fallback failure audit is synthesized. Here no fallback audit is synthesized because a mismatched failed audit is accepted as sufficient.

## Impact

Audit evidence for failed binding calls can misrepresent the failure reason. Policy automation, incident review, billing, and retry logic that consume per-binding audit events can observe `NotFound`, `PermissionDenied`, or any other defined code while the sandbox result reports a different error. This weakens the required-audit contract for bindings that reach outside the sandbox because the runtime verifies only that some failure was audited, not that the audited failure matches the actual failure.

## Suggested tests

Add interpreted and compiled binding-audit tests with an audited binding that writes a failed `BindingCall` event containing all required fields but with an intentionally wrong `ErrorCode`, then throws a `SandboxRuntimeException` with a different code. Assert that execution either fails with a synthesized audit containing the actual code or rejects the binding audit as missing/invalid, and that the accepted audit stream contains the actual failure code.

## Expected behavior

Pass the expected failure code through the audit-sink check, and require failed binding audits emitted after the checkpoint to match that code before treating the required audit as satisfied. If a mismatched failure audit exists, synthesize the correct fallback audit or fail the binding according to the required-audit policy.

## Deduplication key

`correctness/runtime/binding-failure-audit/error-code-mismatch`
