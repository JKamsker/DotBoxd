---
id: COR-0039
area: correctness
status: fixed_pending_verification
priority: high
title: Short-circuit reordering can evaluate throwing pure operands
dedup_key: correctness/short-circuit/reorder-throwing-pure-operands
created_at: 2026-06-12T22:52:31.4830936+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T23:49:31.7651678+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:45:52.9589559+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:49:31.7651678+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0039: Short-circuit reordering can evaluate throwing pure operands

## Claim

Short-circuit boolean evaluation reorders operands based on a pure/cost heuristic, but Safe-IR pure expressions are not guaranteed to be non-throwing. A source expression that should short-circuit before a failing pure numeric expression can instead evaluate the failing operand first and return `InvalidInput`.

## Evidence

`src/DotBoxd.Kernels/Model/ShortCircuitExpressionOrder.cs` chooses the right operand first when both sides are reorderable and the right side is cheaper. `FunctionAnalyzer` marks expressions reorderable when their effects are pure: numeric binary expressions remain reorderable, and function analyses collapse `CanReorder` to `canReorder && IsPure(finalEffects)`.

Both runtimes consume this reordered plan. `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs` calls `ShortCircuitExpressionOrder.Choose` for `&&` and `||`, evaluates `order.First`, and may return without evaluating `order.Second`. `src/DotBoxd.Kernels.Compiler/Emitters/ShortCircuitBooleanEmitter.cs` does the same for compiled IL.

Pure numeric expressions can still throw sandbox errors. `src/DotBoxd.Kernels/Sandbox/SandboxInt32Math.cs` throws `InvalidInput` for division or remainder by zero, and `SandboxNumericOperations.Divide` / `Remainder` route `I32` operands through those helpers.

A minimal shape is `ExpensiveFalse() && ((1 / 0) == 0)` where `ExpensiveFalse` is a pure helper with a higher estimated cost than the literal division side. Source-order short-circuit semantics should return `false` without evaluating the division. The current chooser can evaluate the cheaper right side first and fail with `integer division by zero`. The dual `ExpensiveTrue() || ((1 / 0) == 0)` has the same problem.

## Impact

This changes observable program behavior under validated IR without any host side effect or capability involved. The interpreter and compiled backend can both report `InvalidInput` for a program that should have short-circuited successfully in source order. It also makes behavior depend on cost estimates and helper-function shape rather than language semantics.

## Suggested test

Add interpreted and compiled tests for `&&` and `||` where the left source operand is a pure helper that returns the short-circuiting value after enough pure work to be estimated more expensive, and the right source operand is a pure numeric expression that would throw, such as integer division by zero. Assert both execution modes preserve source-order short-circuit behavior and return `false` for `&&` / `true` for `||` without surfacing `InvalidInput`.

## Suggested fix

Do not reorder short-circuit operands unless the reordered operand is proven total/non-throwing, not merely pure. The conservative fix is to preserve source order for `&&` and `||`. If optimization is still desired, split the analysis into `HasSideEffects` and `CanThrow`/`IsTotal`, and only reorder when both operands are side-effect-free and non-throwing.
