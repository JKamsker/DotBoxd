---
id: PAL-0038
area: perf_alloc
status: open
priority: medium
title: Interpreter call evaluation allocates argument lists per call
dedup_key: alloc/interpreter/call-evaluation/argument-list-per-call
created_at: 2026-06-13T06:24:32.5389570+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:24:32.5389570+00:00
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

# PAL-0038: Interpreter call evaluation allocates argument lists per call

## Claim

Interpreted call evaluation allocates a fresh argument list for every `CallExpression`, including helper calls, binding calls, and fixed-arity collection intrinsics.

## Evidence

- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:109` enters `EvaluateCallAsync` for every interpreted call expression.
- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:111` allocates `new List<SandboxValue>(call.Arguments.Count)` unconditionally.
- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:112` through `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:115` fills that list before dispatch knows whether the target is a fixed-arity collection intrinsic, a private function, or a host binding.
- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:117` through `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:129` then routes the same allocated list through collection helpers, `_interpreter.InvokeFunctionAsync`, or `CallBindingAsync`.
- `benchmarks/DotBoxd.Kernels.Benchmarks/Interpreter/InterpreterExpressionBenchmarks.cs:33` through `benchmarks/DotBoxd.Kernels.Benchmarks/Interpreter/InterpreterExpressionBenchmarks.cs:39` currently benchmark an arithmetic loop, but not repeated interpreted helper/binding/collection calls that expose argument-list allocation.
- Existing `PAL-0013` covers compiled binding argument arrays, and `PAL-0001` covers evaluator object lifetime. This finding is separate: the interpreted call dispatcher allocates argument storage per call even after evaluator reuse and even when compiled mode is not involved.

## Impact

Interpreter mode is used directly, as fallback for unsupported compiled runs, and for debug-trace execution. Modules that call small helpers, cheap host bindings, or collection intrinsics inside loops pay one `List<SandboxValue>` allocation per call before doing the actual runtime work. For low-cost bindings and helper calls, that Gen0 pressure can dominate the interpreted hot path.

## Suggested fix

Avoid heap allocation for common small arities. Use a shared empty argument list for zero-argument calls, a small stack/pooled builder or array-backed value list for one to three arguments, and direct fixed-arity collection intrinsic dispatch where possible. Keep a heap-backed path only for genuinely large arity calls or APIs that require a stable `IReadOnlyList<SandboxValue>` beyond the call.

## Benchmark/allocation test idea

Add BenchmarkDotNet interpreter cases that execute 1,000, 10,000, and 100,000 repeated calls for zero-argument helpers, one/two-argument helpers, pure collection intrinsics, and a cheap synchronous binding. Measure allocated bytes per call and assert the steady-state interpreted dispatcher does not allocate a `List<SandboxValue>` for the common small-arity cases.
