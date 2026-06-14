---
id: COR-0073
area: correctness
status: open
priority: medium
title: RunSummaryAuditFields exposes a mutable dictionary result
dedup_key: correctness/core/run-summary-audit-fields/mutable-result
created_at: 2026-06-13T06:56:20.7265929+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-13T06:56:20.7265929+00:00
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

# COR-0073: RunSummaryAuditFields exposes a mutable dictionary result

## Summary

`RunSummaryAuditFields.Create(...)` returns the mutable `Dictionary<string, string>` it builds, even though the public return type is `IReadOnlyDictionary<string, string>`.

## Evidence

- `src/DotBoxd.Kernels/Model/RunSummaryAuditFields.cs:5` exposes `Create(...)` as a public helper returning `IReadOnlyDictionary<string, string>`.
- `src/DotBoxd.Kernels/Model/RunSummaryAuditFields.cs:16` builds a mutable `Dictionary<string, string>`.
- `src/DotBoxd.Kernels/Model/RunSummaryAuditFields.cs:56` returns that dictionary directly instead of wrapping it with `ModelCopy.StringDictionary(...)` or another read-only collection.
- `tests/DotBoxd.Kernels.Tests/Misc06/PublicModelImmutabilityTests.cs` covers several public model collection copy paths, but the grep/read pass did not find coverage that casts the result of `RunSummaryAuditFields.Create(...)` back to `IDictionary<string, string>` and mutates it.

## Impact

Callers can mutate the structured run-summary field map after creation by casting the returned object to `IDictionary<string, string>` or `Dictionary<string, string>`. That weakens the public immutability contract for audit evidence helpers and can let code accidentally reuse and mutate trusted-looking summary fields before constructing or comparing audit events.

## Suggested test

Add a public model immutability test that calls `RunSummaryAuditFields.Create(...)`, asserts the returned value is not mutable through `IDictionary<string, string>`, and verifies a caller cannot modify `planHash`, `policyHash`, `cacheStatus`, or budget fields after creation.

## Suggested fix

Return a read-only defensive copy from `RunSummaryAuditFields.Create(...)`, for example `ModelCopy.StringDictionary(fields)`, before exposing the dictionary to callers.

## Deduplication

Existing mutability findings cover audit event inputs, validation results, verifier models, sandbox policies, descriptors, and plugin message history. This finding is specific to the public run-summary field factory returning its own mutable result object.
