---
id: ALG-0011
area: perf_algorithm
status: open
priority: medium
title: Required audit checks rescan prior events
dedup_key: algorithm/audit/required-binding-audit/full-event-rescan
created_at: 2026-06-12T22:22:11.9278887+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:22:11.9278887+00:00
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

# ALG-0011: Required audit checks rescan prior events

## Claim

Required binding audit enforcement scans the entire accumulated audit event list after each audited binding call, even though callers pass a checkpoint sequence number for the start of the current binding.

## Evidence

- `src/SafeIR.Core/Sandbox/SandboxContext.cs:147` records an audit checkpoint from `Audit.EventsWritten` before a binding call.
- `src/SafeIR.Interpreter/ExpressionEvaluator.cs:170` records that checkpoint for interpreted binding calls, and `src/SafeIR.Runtime/CompiledBindingDispatcher.cs:10` does the same for compiled binding calls.
- `src/SafeIR.Core/Sandbox/SandboxContext.cs:149` through `src/SafeIR.Core/Sandbox/SandboxContext.cs:159` validates successful required audit emission by calling `Audit.HasBindingAuditSince` with the checkpoint.
- `src/SafeIR.Core/Sandbox/SandboxContext.cs:162` through `src/SafeIR.Core/Sandbox/SandboxContext.cs:185` validates failure audit emission the same way before optionally writing a fallback failure audit.
- `src/SafeIR.Core/Bindings/Audit.cs:59` stores audit events in a `List<SandboxAuditEvent>` and `src/SafeIR.Core/Bindings/Audit.cs:64` exposes the current sequence.
- `src/SafeIR.Core/Bindings/Audit.cs:72` through `src/SafeIR.Core/Bindings/Audit.cs:89` implements `HasBindingAuditSince` as `_events.Any(...)`; the first predicate check is `e.SequenceNumber > checkpoint` at `src/SafeIR.Core/Bindings/Audit.cs:80`, but enumeration still starts at the beginning of `_events` for every call.
- The per-event predicate also checks run id, success, kind, binding id, capability/effect matching, resource id, required fields, and error code at `src/SafeIR.Core/Bindings/Audit.cs:81` through `src/SafeIR.Core/Bindings/Audit.cs:89`.
- This is distinct from `PAL-0021`, which covers copying audit event arrays into execution results. The issue here is repeated in-run scanning of the audit sink while enforcing required binding audits.

## Impact

A run with many audited binding calls pays cumulative audit-log rescans. If each call writes its required audit event, the Nth check still iterates over prior events until it reaches the checkpoint region, pushing total enforcement work toward O(N^2) over the run. This affects both interpreted and compiled execution and is most visible for low-latency bindings that emit per-call audit events.

## Better target

Use the checkpoint as an index or maintain an append-only sequence-indexed structure so `HasBindingAuditSince` examines only events written after the checkpoint. Since `InMemoryAuditSink` owns monotonically increasing sequence numbers, it can translate a checkpoint to a list offset or keep a compact per-binding/per-run recent audit index for required-audit validation.

## Benchmark idea

Add a benchmark that executes 1,000, 10,000, and 100,000 audited binding calls that each emit one valid audit event. Measure total time spent in required audit enforcement before and after changing `HasBindingAuditSince` to inspect only post-checkpoint events.
