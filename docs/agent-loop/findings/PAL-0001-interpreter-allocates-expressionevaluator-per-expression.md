---
id: PAL-0001
area: perf_alloc
status: verified
priority: medium
title: Interpreter allocates ExpressionEvaluator per expression
dedup_key: alloc/interpreter/expression-evaluation/evaluator-object-per-node
created_at: 2026-06-12T20:36:51.9474993+00:00
created_by: perf-reviewer
created_commit: 
updated_at: 2026-06-12T21:27:07.7531392+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:23:02.3143324+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:24:35.0964382+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T21:27:07.7531392+00:00
verified_commit: 
duplicate_of: 
---

# PAL-0001: Interpreter allocates ExpressionEvaluator per expression

## Claim

The interpreter allocates a new `ExpressionEvaluator` object for every expression evaluation, including nested expressions inside loops.

## Evidence

- `InterpreterEvaluator.EvaluateAsync` is a private helper that constructs a fresh evaluator for each call: `new ExpressionEvaluator(...)` at `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:146`.
- `ExpressionEvaluator.EvaluateAsync` recursively evaluates unary, binary, call, and short-circuit expressions, for example at `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:52`, `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:66`, `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:72`, `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:89`, and `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:90`.
- Statement execution invokes the allocating helper for assignments, returns, expression statements, if/while conditions, and for-range bounds at `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:74`, `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:77`, `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:79`, `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:94`, `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:100`, `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:115`, and `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:116`.
- The benchmark project has IPC allocation benchmarks, but no interpreter expression allocation benchmark.

## Impact

This is a statically obvious per-expression allocation on the interpreted hot path. A loop evaluating a binary condition and arithmetic body thousands of times will allocate thousands of short-lived evaluator objects in addition to the actual sandbox values. That can distort interpreter-vs-compiled comparisons and increase Gen0 pressure for hosts that run interpreted mode by policy or fallback.

## Measurement idea

Add a BenchmarkDotNet or allocation test that executes a small interpreted module with 10,000 arithmetic/boolean expression evaluations and records allocated bytes using `MemoryDiagnoser` or `GC.GetAllocatedBytesForCurrentThread`. Compare current behavior to reusing one evaluator per `InterpreterEvaluator` invocation or making evaluator logic static/instance-owned.

## Suggested fix direction

Keep a single `ExpressionEvaluator` instance per `InterpreterEvaluator` execution context, or move expression evaluation methods onto `InterpreterEvaluator` so recursion does not allocate a helper object per expression node.
