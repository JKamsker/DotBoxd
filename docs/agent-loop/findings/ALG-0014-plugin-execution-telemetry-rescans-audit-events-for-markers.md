---
id: ALG-0014
area: perf_algorithm
status: open
priority: medium
title: Plugin execution telemetry rescans audit events for markers
dedup_key: algorithm/plugins/execution-telemetry/audit-event-marker-rescans
created_at: 2026-06-12T23:07:53.0179863+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T23:07:53.0179863+00:00
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

# ALG-0014: Plugin execution telemetry rescans audit events for markers

## Claim

Plugin execution telemetry rescans every result's audit-event list to rediscover run-summary and fallback markers after sandbox execution. This adds O(audit-event-count) telemetry work per kernel entrypoint invocation, separate from the cost of creating or retaining the audit events themselves.

## Evidence

- `src/SafeIR.Plugins/InstalledKernel.cs:271` calls `_executionObserver.Record(entrypoint, _executionMode, result)` after every prepared kernel execution.
- `src/SafeIR.Plugins/Runtime/PluginExecutionObservation.cs:45` calls `result.AuditEvents.LastOrDefault(e => e.Kind == "RunSummary")` to find summary fields.
- `src/SafeIR.Plugins/Runtime/PluginExecutionObservation.cs:46` then calls `result.AuditEvents.FirstOrDefault(e => e.Kind == "ExecutionFallback")`, performing a second pass over the same audit-event list for fallback telemetry.
- Hook-driven `InstalledKernel.InvokeAsync` can run both `ShouldHandle` and `Handle` for one accepted event, so the telemetry rescans can happen twice per plugin per event.
- Existing `PAL-0021` covers redundant copying while building `SandboxExecutionResult.AuditEvents`, and `PAL-0033` covers unbounded retention/copying of plugin execution observations. This finding remains after those are fixed: telemetry extraction still performs full-list marker searches for each recorded execution.

## Impact

Audit event counts grow with binding calls, fallback paths, worker isolation, and richer audit modes. Plugin execution telemetry currently pays repeated scans over those events just to derive fields that were already known when the result was produced. In long-running hosts with busy hooks, this makes diagnostics overhead scale with audit volume for every kernel entrypoint, even when callers only inspect `LastExecution`.

## Better target

Carry run-summary/fallback telemetry as structured execution metadata, or have result construction expose the summary event/fallback reason without searching the full audit list. If the audit list remains the only source, scan once in `Record` and capture both markers in a single pass, preferably using known summary placement instead of general LINQ searches.

## Benchmark/allocation test idea

Add a benchmark or allocation test that records plugin executions with 1, 100, and 10,000 audit events, with and without an `ExecutionFallback` event. Measure `PluginExecutionObserver.Record` time and allocations, and assert telemetry extraction is O(1) or at most one pass over the audit list.
