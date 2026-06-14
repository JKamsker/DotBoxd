---
id: COR-0065
area: correctness
status: fixed_pending_verification
priority: high
title: Worker success payloads bypass result value budget validation
dedup_key: correctness/hosting/worker-result/success-value-budget-validation
created_at: 2026-06-13T06:39:23.3900645+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T07:39:04.3472008+00:00
claimed_by: implementer
claimed_at: 2026-06-13T07:33:51.7500144+00:00
claim_branch: 
fixed_by: implementer
fixed_at: 2026-06-13T07:39:04.3472008+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0065: Worker success payloads bypass result value budget validation

## Claim

Successful worker-process results only type-check the returned `SandboxValue`; they do not validate the returned value's shape against the execution plan budgets or reconcile that shape with the worker-reported usage counters.

## Evidence

`src/DotBoxd.Hosting/SandboxWorkerExecutor.cs` validates a successful worker payload in `WorkerPayloadMatches(...)` by requiring a non-null `Value`, no `Error`, a known entrypoint analysis, and `EntrypointBinder.RequireType(result.Value, analysis.ReturnType, "worker result return type mismatch")`.

`EntrypointBinder.RequireType(...)` delegates to `SandboxValueValidator.RequireType(...)`, which verifies the value kind, declared type, nested element types, and scalar invariants. It does not enforce `ResourceLimits.MaxStringLength`, `MaxTotalStringBytes`, `MaxListLength`, `MaxMapEntries`, `MaxCollectionDepth`, or `MaxTotalCollectionElements`.

`WorkerResourceUsageMatches(...)` then validates only the worker-supplied `SandboxResourceUsage` counters against the plan ceilings. It never recomputes the successful result value shape, so a worker can return an oversized `StringValue`, `ListValue`, or `MapValue` while reporting zero string/collection/allocation usage and emitting a matching zero-usage `RunSummary`.

This is separate from forged budget-ceiling fields in run summaries: the ceilings can now match the plan, while the accepted success payload itself exceeds the plan's value budgets.

## Impact

Worker isolation is the boundary that converts out-of-process data back into trusted `SandboxExecutionResult` values. A buggy or compromised worker can bypass result value quotas, publish impossible resource accounting, and hand callers/audit observers a payload that in-process execution or binding-return charging would have rejected with `QuotaExceeded`. Large strings or collections can also force host-side memory/CPU work after the worker claims the run stayed within budget.

## Suggested test

Add a worker result hardening test with a string-returning entrypoint and a tight policy, for example `MaxStringLength = 4` and `MaxTotalStringBytes = 8`. Have the worker return `SandboxValue.FromString("this is too long")`, report a zero-usage `ResourceMeter(plan.Budget).Snapshot()`, and emit a matching `RunSummary`. The host should reject the worker envelope with `SandboxErrorCode.HostFailure` and `WorkerIsolationFailed` instead of returning the oversized value. Add a collection variant for `MaxListLength` or `MaxTotalCollectionElements`.

## Fix direction

For successful worker results, validate and measure `result.Value` with the entrypoint return type and the plan budgets before accepting the envelope. The validator should fail closed if the returned value exceeds string or collection limits, and it should ensure reported usage is not lower than the accepted result shape where those counters are part of the public accounting contract.
