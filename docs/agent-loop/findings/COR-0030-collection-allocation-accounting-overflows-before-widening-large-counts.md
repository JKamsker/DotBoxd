---
id: COR-0030
area: correctness
status: fixed_pending_verification
priority: high
title: Collection allocation accounting overflows before widening large counts
dedup_key: correctness/resource-quota/collection-copy-allocation-int-overflow
created_at: 2026-06-12T22:33:56.1757820+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-12T23:43:02.6776083+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:42:05.9532987+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:43:02.6776083+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0030: Collection allocation accounting overflows before widening large counts

## Summary
Collection copy allocation charges multiply collection counts by per-element byte constants as `int` values before passing the result to `ChargeAllocation`. Large, policy-permitted list/map sizes can overflow before the quota meter sees the amount, producing a negative or wrapped allocation charge instead of a deterministic `QuotaExceeded` failure.

## Evidence
- `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs` charges `values.Count * 16`, `(source.Values.Count + 1) * 16`, and `Math.Max(1, count) * 32` before the value is widened for `ResourceMeter.ChargeAllocation(long)`.
- `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs` uses the same unchecked shapes for compiled list/map operations.
- `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs` already uses `checked(elementCount * 8)` for `CreateValueArray`, which shows the generated runtime has an explicit fail-closed pattern for size-derived allocation charges, but the collection operations do not use it.
- `ResourceLimitValidation` permits large positive `MaxListLength`, `MaxMapEntries`, `MaxTotalCollectionElements`, and `MaxAllocatedBytes` values, so validation does not cap counts below the overflow thresholds.

## Why it matters
Quota accounting should fail closed as `SandboxErrorCode.QuotaExceeded`. With unchecked `int` arithmetic, a large collection copy can undercharge allocation bytes, or throw `ArgumentOutOfRangeException` from the meter's negative-input guard. In interpreter execution that becomes a generic `HostFailure`; in compiled/runtime direct paths it can similarly escape the intended quota taxonomy. Either outcome is a resource-quota correctness bug, distinct from the existing string-byte overflow finding because the affected counters are collection copy allocation charges.

## Suggested validation
Add focused coverage around the allocation-charge helper used by list/map copy operations. The test should exercise counts just above `int.MaxValue / 16` for list operations and `int.MaxValue / 32` for map operations, and assert the runtime maps overflow to `SandboxErrorCode.QuotaExceeded` rather than recording a wrapped allocation amount or returning `HostFailure`/`ArgumentOutOfRangeException`. If constructing huge concrete collections is impractical, factor the charge computation into an internal checked helper and cover the helper directly plus a smaller integration case proving collection operations call it.

## Suggested fix
Centralize collection allocation charge calculation in a checked helper that widens before multiplication, for example `checked((long)count * bytesPerElement)`, and map overflow to `SandboxErrorCode.QuotaExceeded`. Use the same helper from interpreter and compiled collection paths before allocating or copying collections.
