---
id: COR-0059
area: correctness
status: fixed_pending_verification
priority: high
title: Collection equality validates but compares backing collection identity
dedup_key: correctness:collection-equality-backing-identity
created_at: 2026-06-13T06:28:36.4765420+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T07:31:36.0966848+00:00
claimed_by: implementer
claimed_at: 2026-06-13T07:29:19.5461124+00:00
claim_branch: 
fixed_by: implementer
fixed_at: 2026-06-13T07:31:36.0966848+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0059: Collection equality validates but compares backing collection identity

## Evidence

`FunctionAnalyzer.AnalyzeBinary` accepts `==` and `!=` whenever the left and right operands have the same `SandboxType`, including `List<T>` and `Map<K,V>` values (`src/SafeIR.Validation/FunctionAnalyzer.cs`). The interpreter and compiled runtime then evaluate equality with `Equals(left, right)` (`src/SafeIR.Interpreter/ExpressionEvaluator.cs`, `src/SafeIR.Runtime/CompiledRuntime.cs`).

Collection sandbox values are records whose `Values` fields are `IReadOnlyList<SandboxValue>` or `IReadOnlyDictionary<SandboxValue, SandboxValue>` (`src/SafeIR.Core/Sandbox/SandboxValue.cs`). Those backing collection interfaces use reference equality, so two independently materialized collections with the same elements do not compare structurally.

A validated expression such as `list.of(1) == list.of(1)` therefore type-checks but can evaluate to `false` because each `list.of` call constructs a distinct backing list (`src/SafeIR.Interpreter/Internal/CollectionOperations.cs`). Maps have the same issue for independently constructed equal maps.

## Impact

Safe-IR exposes equality on collection-typed expressions but returns identity-based answers that disagree with value semantics. Branches, policies, and binding decisions can take the wrong path when equivalent collection values are reconstructed instead of reusing the same backing instance.

## Fix direction

Either implement deterministic structural equality for sandbox list and map values in the shared runtime equality path, or reject collection equality in validation and document equality as scalar-only. Add interpreter and compiled-runtime regression tests for equal and unequal lists/maps constructed from separate expressions.
