---
id: COR-0042
area: correctness
status: fixed_pending_verification
priority: high
title: Collection literal validation skips nested scalar safety checks
dedup_key: correctness/validation/nested-collection-literals-skip-scalar-safety
created_at: 2026-06-12T23:02:28.1805143+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-12T23:07:04.4523180+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:04:05.9052703+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:07:04.4523180+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0042: Collection literal validation skips nested scalar safety checks

## Problem

Programmatic collection literals validate only their container shape, not the scalar literal constraints of nested values. `DangerousReferenceDetector.CheckLiteral` inspects top-level scalar literals, but does not recurse into `ListValue` or `MapValue`. `LiteralExpressionAnalyzer.Analyze` validates string, opaque ID, F64, path, and URI constraints only when the top-level literal value has that scalar type. When the top-level literal is a `ListValue` or `MapValue`, it delegates to `SandboxValueValidator.RequireType(...)`, which checks type shape and known value kinds but does not enforce finite F64 values, path/URI constraints, text literal length limits, or forbidden CLR/IL descriptor detection for nested scalar values.

## Impact

A host or generator that constructs DotBoxd.Kernels through the public object model can create a validated collection literal containing values that scalar literal validation would reject at the top level, such as a nested `F64Value(double.NaN)`, forbidden descriptor text like `System.IO.File.ReadAllText`, or record-constructed invalid path/URI values. That weakens the programmatic IR validation boundary and makes literal safety depend on whether a value is wrapped in a list/map.

## Evidence

- `src/DotBoxd.Kernels.Validation/Internal/DangerousReferenceDetector.cs` handles scalar `LiteralExpression` values but assigns no text to inspect for `ListValue` or `MapValue` literals.
- `src/DotBoxd.Kernels.Validation/Internal/LiteralExpressionAnalyzer.cs` applies scalar-specific checks only to top-level `StringValue`, `OpaqueIdValue`, `F64Value`, `SandboxPathValue`, and `SandboxUriValue`, then treats `ListValue`/`MapValue` as allocation only.
- `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs` recursively checks declared sandbox types, but it does not check finite F64 values, path/URI textual validity, max text literal length, or forbidden descriptor text for nested scalar values.
- `tests/DotBoxd.Kernels.Tests/Misc06/ProgrammaticIrValidationTests.cs` already exercises programmatic collection literals, so this is on a supported construction path even though JSON import does not expose collection literal syntax.

## Fix direction

Make literal validation recursively walk `ListValue` and `MapValue` contents and apply the same scalar literal checks and dangerous-reference detection to every nested key/value. Add regression tests for nested collection literals containing a forbidden descriptor string, a non-finite F64 record value, and an invalid record-constructed path/URI value; validation should reject them before execution.
