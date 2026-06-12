---
id: PAL-0033
area: perf_alloc
status: open
priority: medium
title: Plugin execution observer retains every run observation
dedup_key: alloc/plugins/execution-observer/unbounded-observation-history
created_at: 2026-06-12T22:47:22.8428621+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:47:22.8428621+00:00
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

# PAL-0033: Plugin execution observer retains every run observation

## Claim

Installed plugin kernels retain every execution observation in memory and copy the full history whenever observations are read.

## Evidence

- `src/SafeIR.Plugins/InstalledKernel.cs` exposes `LastExecution` and `ExecutionObservations`, and records one observation after every prepared sandbox execution in `ExecutePreparedAsync`.
- `src/SafeIR.Plugins/Runtime/PluginExecutionObservation.cs` stores observations in a private `List<PluginExecutionObservation>`.
- `PluginExecutionObserver.Record` appends a new `PluginExecutionObservation` for each `ShouldHandle` and `Handle` execution and never removes old entries.
- `PluginExecutionObserver.Snapshot` returns `_observations.ToArray()`, so reading `InstalledKernel.ExecutionObservations` allocates and copies the entire retained history.
- Existing retention findings cover auto execution hotness (`PAL-0030`) and compiled executable cache materialization (`PAL-0031`). This finding is separate: it is per-installed-plugin execution telemetry retained by `PluginExecutionObserver`.

## Impact

Long-running plugin hosts can process thousands or millions of events through the same installed kernel. The observer retains at least one record per `ShouldHandle` execution and often another per `Handle`, making memory usage grow with event count rather than active state. Diagnostics code that reads `ExecutionObservations` also performs an O(n) allocation and copy of that history, which becomes more expensive as the host runs.

## Measurement idea

Add an allocation/retention test or benchmark that invokes a kernel repeatedly through `ShouldHandleAsync`/`HandleAsync`, then asserts retained observation count and allocated bytes when reading `ExecutionObservations`. Compare current behavior to a bounded ring buffer or opt-in history setting.

## Suggested fix direction

Keep `LastExecution` as the always-on hot-path diagnostic and make full history bounded or explicitly opt-in. If history is retained by default, use a configurable ring buffer and document the retention limit. Snapshot should copy only the bounded window.
