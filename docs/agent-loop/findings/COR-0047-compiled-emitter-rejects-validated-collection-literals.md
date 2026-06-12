---
id: COR-0047
area: correctness
status: open
priority: medium
title: Compiled emitter rejects validated collection literals
dedup_key: correctness/compiled/validated-collection-literals-unemittable
created_at: 2026-06-12T23:16:33.6029810+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-12T23:16:33.6029810+00:00
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

# COR-0047: Compiled emitter rejects validated collection literals

## Problem

Programmatic list/map literals are accepted by validation and by the interpreter, but the compiled emitter cannot emit them. `LiteralExpressionAnalyzer.Analyze` validates any `LiteralExpression` by calling `SandboxValueValidator.RequireType(value, value.Type, ...)` and then returns `literal.Value.Type`, so a valid `ListValue` or `MapValue` literal can be prepared as a normal pure module. The interpreter then executes the same literal path through `ExpressionEvaluator.ChargeLiteral(...)` and charges the value shape at runtime.

The compiler takes a different path: `CompiledLiteralEmitter.Emit(...)` handles only scalar literal cases and falls through to `throw Unsupported("literal not supported by compiler")` for `ListValue` and `MapValue`. `SandboxHost.TryExecuteCompiledAsync(...)` converts that `SandboxRuntimeException` into a failed compiled result when `AllowFallbackToInterpreter = false`, even though the module has already passed validation and the interpreter can execute it.

## Impact

A host or generator using the public object model can prepare a valid pure module that returns or assigns a collection literal, but strict compiled execution fails before dispatch with a validation error. That makes execution semantics depend on mode selection: interpreted mode succeeds and charges policy budgets, default compiled mode may silently fall back, and compiled-without-fallback fails for the same validated IR. Auto mode can also promote a warmed pure module into this fallback/failure path once the selector chooses compiled execution.

## Evidence

- `src/SafeIR.Validation/Internal/LiteralExpressionAnalyzer.cs` validates `LiteralExpression.Value` generically and returns `literal.Value.Type`, so `ListValue` and `MapValue` literals are accepted when their contents match their declared shape.
- `src/SafeIR.Core/Model/CanonicalModuleHasher.cs` includes `ListValue` and `MapValue` in canonical module serialization, confirming these literals are part of prepared module identity.
- `src/SafeIR.Interpreter/ExpressionEvaluator.cs` evaluates all literals through `ChargeLiteral(...)`, so interpreted execution supports and charges collection literals.
- `src/SafeIR.Compiler/Emitters/CompiledLiteralEmitter.cs` omits `ListValue` and `MapValue` cases and throws `ValidationError` via `Unsupported("literal not supported by compiler")`.
- `tests/SafeIR.Tests/Misc06/ProgrammaticIrValidationTests.cs` already treats programmatic collection literals as supported validation/runtime inputs, but only exercises interpreted execution.

## Suggested fix

Teach `CompiledLiteralEmitter` to emit `ListValue` and `MapValue` literals through existing runtime helpers such as `CompiledRuntime.ListOf`, `CompiledRuntime.ListEmpty`, and map construction helpers, preserving the same metering and type validation as interpreted execution. If collection literals are intentionally unsupported in compiled mode, reject them during validation or mark the function as not compilable so strict compiled execution does not fail after preparation.

## Regression coverage

Add a compiled-mode regression test that prepares a pure module returning a `ListValue` literal and a `MapValue` literal, executes it with `Mode = ExecutionMode.Compiled` and `AllowFallbackToInterpreter = false`, and asserts success with `ActualMode == ExecutionMode.Compiled`. Keep the existing interpreted quota test for collection literal charging and add a compiled quota variant so both modes enforce the same policy limits.
