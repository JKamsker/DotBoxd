---
id: COR-0043
area: correctness
status: fixed_pending_verification
priority: medium
title: Deterministic binding failure audits use wall-clock timestamps
dedup_key: correctness/runtime/deterministic-binding-failure-audit-uses-wall-clock
created_at: 2026-06-12T23:02:29.8762248+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-13T00:40:26.2871949+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:38:39.2047911+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:40:26.2871949+00:00
fixed_commit: pending
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0043: Deterministic binding failure audits use wall-clock timestamps

## Problem

Required binding failure audits use the host wall clock even under deterministic policies. `SandboxContext.EnsureRequiredBindingFailureAudit(...)` synthesizes a `BindingCall` failure audit with `DateTimeOffset.UtcNow`. Successful deterministic time/random bindings use the context audit timestamp, but a deterministic binding can fail before it writes its own audit. For example, `SafeRandomBindings.NextI32` calls `context.NextRandomInt32(min, max)` before writing the success audit; an invalid range throws, the dispatcher calls `EnsureRequiredBindingFailureAudit(...)`, and the resulting audit timestamp comes from the real clock instead of the policy logical clock.

## Impact

The same deterministic plan and input can produce different audit evidence across replays when an audited deterministic binding fails before emitting its own event. That breaks deterministic execution expectations for failure traces and makes replay/test comparison of audit streams non-deterministic even though the policy supplies `LogicalNow` and `RandomSeed`.

## Evidence

- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs` exposes `AuditTimestamp()` for deterministic audit time, but `EnsureRequiredBindingFailureAudit(...)` uses `DateTimeOffset.UtcNow` when writing fallback failure audits.
- `src/DotBoxd.Kernels.Runtime/Bindings/SafeRandomBindings.cs` validates the random range by calling `context.NextRandomInt32(...)` before writing its own audited success event, so invalid ranges rely on the synthesized failure audit.
- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs` and `src/DotBoxd.Kernels.Runtime/CompiledBindingDispatcher.cs` both call `EnsureRequiredBindingFailureAudit(...)` when a binding throws before producing required audit evidence.

## Fix direction

Use `AuditTimestamp()` for synthesized binding failure audits, and review other sandbox-generated audit events in `SandboxContext` for the same deterministic-clock rule. Add interpreted and compiled regression tests for a deterministic `random.nextI32` call with an invalid range, asserting the synthesized failure audit timestamp equals the policy `LogicalNow`.
