---
id: COR-0069
area: correctness
status: open
priority: medium
title: Null scalar payloads crash value-boundary validation before fail-closed errors
dedup_key: correctness/runtime-value-boundaries/null-scalar-payload-prevalidation-crash
created_at: 2026-06-13T06:50:56.5277338+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T06:50:56.5277338+00:00
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

# COR-0069: Null scalar payloads crash value-boundary validation before fail-closed errors

## Claim

Public scalar `SandboxValue` records can still carry null payloads that crash validation or metering before the runtime reaches the hardened scalar invariant checks. Instead of failing closed as `ValidationError`/`InvalidInput`, programmatic literals can throw host exceptions during prepare and entrypoint inputs can be reported as generic `HostFailure`.

## Evidence

- `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:20` exposes `SandboxValue.FromString(string value)` without a null guard, and `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:86` exposes the public `StringValue(string Value)` record constructor/init path.
- `src/DotBoxd.Kernels.Validation/Internal/LiteralValueSafety.cs:46` handles `StringValue` by calling `EnsureTextLiteralLength(text.Value, "string")`; that helper dereferences `value.Length`, so `new StringValue(null!)` crashes literal validation before `LiteralExpressionAnalyzer` can convert it into an `E-CONST-*` diagnostic.
- Interpreted and compiled entrypoint execution charge input shape before binding/type validation: `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:31` and `src/DotBoxd.Hosting/Execution/CompiledExecutionRunner.cs:36` call `ChargeValue(input)` before `EntrypointBinder` validates the value.
- `src/DotBoxd.Kernels/Sandbox/Values/SandboxValueShapeMeter.cs:34` to `src/DotBoxd.Kernels/Sandbox/Values/SandboxValueShapeMeter.cs:35` sends `StringValue.Value` to `SandboxLiteralConstraints.TextShape(...)`, and `src/DotBoxd.Kernels/Sandbox/SandboxLiteralConstraints.cs:115` computes shape from that string. A null payload therefore throws before `SandboxValueValidator.RequireType(...)` can reject it.
- Binding returns use the validated meter, but `src/DotBoxd.Kernels/Sandbox/Values/SandboxValidatedValueShapeMeter.cs:39` to `src/DotBoxd.Kernels/Sandbox/Values/SandboxValidatedValueShapeMeter.cs:40` still dereference `StringValue.Value` after scalar invariant checks that do not include string nullability.
- Existing value-boundary tests cover non-finite doubles and invalid path/URI records, but they do not cover `StringValue(null!)` or null nested string payloads.

## Impact

A public model instance that should be rejected as malformed sandbox data can instead escape the sandbox error taxonomy. Programmatic modules may fail prepare with an implementation exception, while runtime inputs or binding returns can become generic host failures. That weakens deterministic result classification and leaves a remaining null-payload gap after the malformed scalar record hardening.

## Suggested fix

Reject null scalar payloads at construction or in shared scalar validation before any metering/literal safety code dereferences them. Add regression coverage for `new StringValue(null!)` as a programmatic literal, entrypoint input in interpreted and compiled modes, and binding return value. The expected result should be a validation diagnostic or sandbox `InvalidInput`/`BindingFailure`, not `NullReferenceException` or generic `HostFailure`.
