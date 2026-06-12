---
id: COR-0037
area: correctness
status: open
priority: medium
title: Oversized wall-time limits can fail timeout token setup
dedup_key: correctness:oversized-walltime-cancelafter-hostfailure
created_at: 2026-06-12T22:48:35.4106981+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-12T22:48:35.4106981+00:00
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

# COR-0037: Oversized wall-time limits can fail timeout token setup

## Claim

Very large non-negative wall-time limits can survive validation and later produce host-failure behavior when file or HTTP bindings pass the remaining wall time to cancellation APIs.

## Evidence

`src/SafeIR.Core/Model/ResourceLimitValidation.cs` only rejects negative `ResourceLimits.MaxWallTime` values. `src/SafeIR.Core/Model/Resources.cs` accepts those limits, caps its internal deadline arithmetic, and `RemainingWallTime()` can return `TimeSpan.MaxValue` for a far-future deadline.

`src/SafeIR.Core/Sandbox/SandboxContext.cs` exposes `CreateWallTimeToken()` that calls `CancelAfter(Budget.RemainingWallTime())`. `src/SafeIR.Runtime/Bindings/SafeFileSystem.cs` similarly creates a linked token and calls `timeout.CancelAfter(remaining)`. `src/SafeIR.Transport.Http/SafeHttpClient.cs` computes `EffectiveTimeout(...)` from `RemainingWallTime()` and passes it to `CancelAfter`.

`CancellationTokenSource.CancelAfter(TimeSpan)` rejects delays outside its supported range. With a policy such as `new ResourceLimits(MaxWallTime: TimeSpan.MaxValue)`, the limit is considered valid but binding setup can throw an argument error that is converted to a generic host failure rather than applying a bounded timeout or rejecting the policy as invalid.

## Impact

Sandbox policy validation says the wall-time budget is valid, but resource-enforced bindings can fail before doing work with `HostFailure` semantics. That makes timeout behavior depend on the magnitude of an otherwise accepted policy value and breaks the expectation that invalid budgets are rejected up front while exhausted budgets produce `Timeout`.

## Suggested fix

Define and validate a maximum supported wall-time value that is safe for every downstream API using it, or clamp `RemainingWallTime()` before every `CancelAfter` call. Add tests that prepare and execute file and HTTP bindings with an oversized positive `MaxWallTime` and assert the result is deterministic: either policy validation rejects it, or binding execution receives a supported cancellation delay and does not surface `HostFailure` from timeout token setup.
