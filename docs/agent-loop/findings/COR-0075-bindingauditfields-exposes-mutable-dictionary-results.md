---
id: COR-0075
area: correctness
status: open
priority: medium
title: BindingAuditFields exposes mutable dictionary results
dedup_key: correctness/core/binding-audit-fields/mutable-results
created_at: 2026-06-13T07:02:12.8441710+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-13T07:02:12.8441710+00:00
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

# COR-0075: BindingAuditFields exposes mutable dictionary results

## Summary

`BindingAuditFields.Create(...)` exposes mutable dictionary instances through an `IReadOnlyDictionary<string, string>` return type. Both public overloads return the mutable `Dictionary<string, string>` they build, so callers can cast the result back to `IDictionary<string, string>` or `Dictionary<string, string>` and change binding audit fields after creation.

## Evidence

- `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:5` exposes the module/policy overload as `public static IReadOnlyDictionary<string, string> Create(...)`.
- `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:14` to `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:18` builds a mutable dictionary with `.ToDictionary(...)`, adds `moduleHash` and `policyHash`, and returns that dictionary directly.
- `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:21` exposes the simpler overload as `public static IReadOnlyDictionary<string, string> Create(...)`.
- `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:26` to `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:32` builds a mutable `Dictionary<string, string>`, adds optional byte fields, and returns it directly.
- `tests/DotBoxd.Kernels.Tests/Misc06/PublicModelImmutabilityTests.cs` covers model collection copies and `SandboxAuditEvent` field input copying, but it does not assert that `BindingAuditFields.Create(...)` results are not mutable through dictionary casts.
- Existing `PAL-0024` covers duplicate dictionary allocation in this helper. That is a performance finding, not the public immutability contract issue.

## Impact

Binding audit fields are structured evidence helpers. A caller can mutate `resourceKind`, `durationMs`, `bytesRead`, `bytesWritten`, `moduleHash`, or `policyHash` after `Create(...)` returns despite the read-only return type. This can lead to accidental reuse or pre-publication mutation of trusted-looking audit field maps and is inconsistent with the defensive-copy behavior used by public model and audit-event boundaries.

## Suggested test

Add a public model immutability test that calls both `BindingAuditFields.Create(...)` overloads, casts each result to `IDictionary<string, string>` if possible, and verifies the result cannot be mutated. Cover at least `resourceKind`, `durationMs`, and the module/policy hash fields.

## Fix direction

Return a read-only defensive copy from both overloads, for example `ModelCopy.StringDictionary(fields)` or a `ReadOnlyDictionary<string, string>` wrapper around an owned dictionary, before exposing the result to callers. Keep the existing audit-event constructor copy as a separate boundary, but do not expose mutable helper results from public APIs.

## Deduplication

Existing mutability findings cover audit event inputs, validation results, verifier models, sandbox policies, descriptors, plugin message history, and `RunSummaryAuditFields.Create(...)`. This finding is specific to the public binding-audit field factory returning mutable dictionary objects.
