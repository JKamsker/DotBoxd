---
id: PAL-0021
area: perf_alloc
status: fixed_pending_verification
priority: medium
title: Execution result audit events are copied multiple times
dedup_key: alloc/audit-events/result-double-copy
created_at: 2026-06-12T22:15:11.4297947+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T07:49:31.8010775+00:00
claimed_by: fixer
claimed_at: 2026-06-13T07:49:31.6693524+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-13T07:49:31.8010775+00:00
fixed_commit: b14fd0a
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# PAL-0021: Execution result audit events are copied multiple times

## Claim

Execution result creation copies audit event lists twice on normal interpreted/compiled runs, and resequencing paths can add another full copy.

## Evidence

- `InMemoryAuditSink.Events` returns `_events.ToArray()` at `src/DotBoxd.Kernels/Bindings/Audit.cs:62`, allocating a fresh array snapshot.
- `SandboxExecutionResult.AuditEvents` copies any assigned sequence through `ModelCopy.List(value)` at `src/DotBoxd.Kernels/ExecutionPlan.cs:119` through `src/DotBoxd.Kernels/ExecutionPlan.cs:122`.
- `ModelCopy.List` allocates another array with `values.ToArray()` and wraps it in a `ReadOnlyCollection<T>` at `src/DotBoxd.Kernels/Model/ModelCopy.cs:5` through `src/DotBoxd.Kernels/Model/ModelCopy.cs:9`.
- The interpreted runner assigns `AuditEvents = audit.Events` at `src/DotBoxd.Kernels.Interpreter/SandboxInterpreter.cs:70`, so every interpreted result snapshots the sink and then copies that snapshot into the result.
- The compiled runner does the same at `src/DotBoxd.Hosting/Execution/CompiledExecutionRunner.cs:76`.
- Host-side failure result builders repeat the same pattern, for example `src/DotBoxd.Hosting/SandboxHost.Results.cs:53`, `src/DotBoxd.Hosting/SandboxHost.Results.cs:87`, `src/DotBoxd.Hosting/SandboxHost.Results.cs:125`, `src/DotBoxd.Hosting/SandboxHost.Results.cs:156`, `src/DotBoxd.Hosting/SandboxHost.Results.cs:199`, `src/DotBoxd.Hosting/SandboxHost.Results.cs:238`, and `src/DotBoxd.Hosting/SandboxHost.Results.cs:271`.
- `SandboxAuditEventSequence.ToSequencedArray` builds a new sink and then returns `sink.Events.ToArray()` at `src/DotBoxd.Hosting/SandboxHost.Results.cs:332` through `src/DotBoxd.Hosting/SandboxHost.Results.cs:338`; because `sink.Events` already allocated an array, the trailing `ToArray()` copies it again before the `SandboxExecutionResult` init setter may copy it once more.
- Existing `COR-0014` covers public audit immutability. This finding is distinct: after snapshotting was introduced, the current result path now performs redundant full-list copies.

## Impact

Audit event count can grow with per-call, per-resource, or full input/output audit levels. Every run currently allocates at least two arrays proportional to audit event count just to move events from the sink into the public result, before consumers or observers inspect them. Fallback and worker resequencing paths can allocate additional full copies. This is avoidable allocation on execution hot paths that already track audit events in memory.

## Better target

Provide a single ownership-transfer/snapshot API from `InMemoryAuditSink` to an immutable result list, or teach `SandboxExecutionResult` to accept an already-owned immutable array/read-only collection without copying again. Resequencing should return the sink snapshot directly rather than calling `ToArray()` on an array snapshot.

## Benchmark/allocation test idea

Add an allocation benchmark that executes modules with summary-only, per-call, and per-resource audit levels producing 1, 100, and 10,000 events. Measure allocated bytes from result construction and fallback/worker resequencing, with a regression assertion that event transfer performs one O(event-count) snapshot rather than multiple full copies.
