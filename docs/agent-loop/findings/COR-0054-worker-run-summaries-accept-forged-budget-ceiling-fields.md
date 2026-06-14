---
id: COR-0054
area: correctness
status: fixed_pending_verification
priority: medium
title: Worker run summaries accept forged budget ceiling fields
dedup_key: correctness/hosting/worker-run-summary/forged-budget-ceilings
created_at: 2026-06-12T23:30:30.8201166+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T00:36:01.2796582+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:33:32.5633349+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:36:01.2796582+00:00
fixed_commit: pending
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0054: Worker run summaries accept forged budget ceiling fields

## Claim

Worker run-summary validation accepts forged budget ceiling fields for every resource limit except fuel. A worker can return a valid `SandboxExecutionResult` with usage counters inside the actual plan budget while the accepted `RunSummary` audit fields advertise different `maxLoopIterations`, `maxAllocatedBytes`, host-call, I/O, log, collection, or string ceilings.

## Evidence

`src/DotBoxd.Kernels/Model/RunSummaryAuditFields.cs` emits both observed usage and budget ceiling fields into every run summary, including `maxLoopIterations`, `maxAllocatedBytes`, `maxHostCalls`, `maxFileBytesRead`, `maxFileBytesWritten`, `maxNetworkBytesRead`, `maxNetworkBytesWritten`, `maxLogEvents`, `maxCollectionElements`, and `maxStringBytes`.

`src/DotBoxd.Hosting/WorkerAuditValidator.cs` allows those field names in worker-supplied `RunSummary` events and only checks that the text is control-character-free.

`src/DotBoxd.Hosting/SandboxWorkerExecutor.cs` then validates worker summaries in `WorkerRunSummaryMatches(...)`. It requires the plan hashes, usage counters, `fuelUsed`, and `maxFuel` to match, but it never compares the other `max*` fields against `plan.Budget` or the expected run-summary fields. Because `SandboxResourceUsage` carries only `MaxFuel`, the skipped max fields are accepted solely through the audit field dictionary.

This is distinct from `COR-0027`, which covers forged non-summary audit events. Here the forged evidence is inside the single required `RunSummary` event that the worker validator otherwise treats as authoritative.

## Impact

Accepted worker-isolated executions can publish audit summaries whose budget ceilings disagree with the execution plan that was actually enforced. Downstream audit exporters, billing, SLO monitors, or compliance checks that read run-summary fields can be told that a run had a larger or smaller loop, allocation, host-call, file/network, log, collection, or string budget than it actually had. The top-level usage bounds still protect execution, but the host-trusted audit evidence is incorrect.

## Suggested tests

Extend worker result hardening tests with a worker that returns a valid successful result and valid usage counters, but changes one run-summary field such as `maxAllocatedBytes` or `maxHostCalls` away from `plan.Budget`. The host should reject the worker envelope with `SandboxErrorCode.HostFailure`. Repeat for at least one long-valued ceiling and one int-valued ceiling.

## Expected behavior

Worker run-summary validation should compare every emitted budget ceiling field against the current plan budget, or generate the accepted run summary on the host from validated result data instead of trusting worker-supplied ceiling fields.

## Deduplication key

`correctness/hosting/worker-run-summary/forged-budget-ceilings`
