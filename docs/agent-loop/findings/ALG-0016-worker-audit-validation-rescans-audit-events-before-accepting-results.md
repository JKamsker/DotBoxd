---
id: ALG-0016
area: perf_algorithm
status: open
priority: medium
title: Worker audit validation rescans audit events before accepting results
dedup_key: algorithm/worker-audit-validation/result-event-rescans
created_at: 2026-06-12T23:16:30.9790985+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T23:16:30.9790985+00:00
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

# ALG-0016: Worker audit validation rescans audit events before accepting results

## Claim

Worker-process execution validates every accepted worker result by scanning `result.AuditEvents` multiple times and materializing a summary array. That makes host-side worker result validation pay repeated O(audit-event-count) work plus an allocation on every worker-isolated sandbox execution before the result is published.

## Evidence

- `src/SafeIR.Hosting/SandboxWorkerExecutor.cs` validates worker results in `ValidateWorkerResult`, and every worker-process execution reaches `WorkerAuditMatches` before accepted results are returned.
- `WorkerAuditMatches` reads the first run id, then scans all audit events with `result.AuditEvents.Any(e => e.RunId != runId)` to enforce a common run id.
- The same method immediately loops over every audit event again and calls `WorkerAuditValidator.Matches(...)`, which performs per-event safety/schema checks in `src/SafeIR.Hosting/WorkerAuditValidator.cs`.
- `WorkerAuditMatches` then scans the same list a third time with `result.AuditEvents.Where(e => e.Kind == "RunSummary").ToArray()`, allocating an array just to require one run summary and compare it with the result.
- On success, `SandboxWorkerExecutor.ExecuteAsync` still resequences the accepted events with `result.AuditEvents.ToSequencedArray()` before returning the result to `SandboxHost.Publish`.
- Existing `PAL-0021` covers redundant copying while constructing/resequencing execution-result audit events, `ALG-0011` covers in-run required-binding audit rescans, and `ALG-0014` covers plugin telemetry marker scans. This finding is separate: worker isolation boundary validation performs repeated full-list scans and a summary array allocation for every worker result before the host trusts or publishes it.

## Impact

Worker-process isolation is the production path intended for hardened hosts. Valid worker results can contain audit events proportional to binding calls, debug trace, cache invalidation, fallback, and richer audit modes. A high-frequency host using worker isolation pays multiple host-side passes over that audit list for every sandbox call even when the worker result is valid, so validation overhead scales with audit volume rather than with the single boundary decision the host needs to make.

## Better target

Validate the worker audit envelope in one pass. Track the expected run id, summary count, and summary reference while applying `WorkerAuditValidator.Matches` to each event, then validate the single captured summary after the loop without `Where(...).ToArray()`. Keep resequencing semantics, but avoid duplicate validation scans and summary-array allocation.

## Benchmark/allocation test idea

Add a worker-isolation benchmark or allocation test with a fake worker returning valid results containing 1, 100, and 10,000 audit events plus exactly one `RunSummary`. Measure `SandboxHost.ExecuteAsync` with `SandboxIsolation.WorkerProcess`, and assert accepted worker-result validation performs at most one audit-event pass and does not allocate a run-summary array.
