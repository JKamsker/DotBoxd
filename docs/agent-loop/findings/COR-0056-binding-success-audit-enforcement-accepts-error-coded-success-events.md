---
id: COR-0056
area: correctness
status: fixed_pending_verification
priority: medium
title: Binding success audit enforcement accepts error-coded success events
dedup_key: correctness/runtime/binding-success-audit/error-code-on-success
created_at: 2026-06-12T23:33:14.5855875+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T00:32:59.7277703+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:29:10.4379906+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:32:59.7277703+00:00
fixed_commit: pending
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0056: Binding success audit enforcement accepts error-coded success events

## Claim

Required binding success audit enforcement accepts successful binding audit events that also carry an error code. A binding can satisfy the required success-audit contract with contradictory evidence such as `Success = true` and `ErrorCode = SandboxErrorCode.PermissionDenied`.

## Evidence

`src/DotBoxd.Kernels/Sandbox/SandboxContext.cs` calls `Audit.HasBindingAuditSince(descriptor, checkpoint, success: true, RunId, ModuleHash, PolicyHash)` from `EnsureRequiredBindingSuccessAudit(...)` after a binding returns successfully.

`src/DotBoxd.Kernels/Bindings/Audit.cs` implements `InMemoryAuditSink.HasBindingAuditSince(...)` by checking sequence number, run id, `e.Success == success`, binding id, capability, effect, resource id, and required fields. The final predicate is `(success || e.ErrorCode is not null)`, so when `success` is true the check does not require `e.ErrorCode` to be null.

By contrast, worker audit validation in `src/DotBoxd.Hosting/WorkerAuditValidator.cs` explicitly rejects contradictory audit events with `(auditEvent.Success && auditEvent.ErrorCode is not null)`. The in-process required binding audit gate has no equivalent check before accepting the event as satisfying the binding's audit requirement.

This is distinct from `COR-0055`, which covers failed binding audits whose error code does not match the thrown error. This issue covers successful binding calls that can be audited as both successful and error-bearing.

## Impact

Bindings with required audit levels can return successfully while publishing contradictory audit evidence. Downstream audit consumers can see a successful `BindingCall`, `SandboxLog`, or `PluginMessage` with an error code and may classify the operation as denied, failed, or suspicious despite the sandbox result succeeding. Because the runtime accepts the event as satisfying the required audit contract, it does not synthesize or require a clean success audit.

## Suggested tests

Add interpreted and compiled tests with an audited binding that writes a structurally valid success audit after the checkpoint but sets `ErrorCode = SandboxErrorCode.PermissionDenied`, then returns the expected value. Assert that required-audit enforcement rejects the contradictory audit or requires a clean success audit with `ErrorCode is null` before the sandbox result can succeed.

## Expected behavior

Successful binding audits used to satisfy required audit enforcement should require `ErrorCode is null`. Failed binding audits should require a defined, matching error code. The in-process audit gate should enforce the same success/error consistency that worker audit validation already enforces.

## Deduplication key

`correctness/runtime/binding-success-audit/error-code-on-success`
