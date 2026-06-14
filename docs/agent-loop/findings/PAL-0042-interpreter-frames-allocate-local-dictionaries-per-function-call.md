---
id: PAL-0042
area: perf_alloc
status: open
priority: medium
title: Interpreter frames allocate local dictionaries per function call
dedup_key: alloc/interpreter/function-invocation/local-dictionary-per-call
created_at: 2026-06-13T06:34:42.6560068+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:34:42.6560068+00:00
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

# PAL-0042: Interpreter frames allocate local dictionaries per function call

## Claim

The interpreter allocates a string-keyed local dictionary for every function invocation, including helper calls inside loops. Even fixed-shape functions with known parameters and locals use `Dictionary<string, SandboxValue>` lookup/storage rather than an indexed frame shape prepared once per function.

## Evidence

- `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs` calls `InterpreterFrame.Create(function, args)` at the start of every `InvokeFunctionAsync`.
- `src/DotBoxd.Kernels.Interpreter/InterpreterFrame.cs` allocates `new Dictionary<string, SandboxValue>(StringComparer.Ordinal)` and populates parameters by name for every frame.
- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs` resolves helper calls through `_interpreter.InvokeFunctionAsync(function, args)`, so loops that call small helpers allocate one local dictionary per helper invocation.
- Local reads and writes use `frame.Locals[...]` in `ExpressionEvaluator` and `InterpreterEvaluator`, keeping the runtime path tied to string hashing even though validation has already seen the function's local/parameter names.
- Existing `PAL-0038` covers the caller-side argument list allocation. This finding is the callee frame/local storage allocation that remains after argument list allocation is removed.

## Impact

Interpreted mode, fallback mode, and debug-trace mode can execute small helper functions many times. For cheap helpers, dictionary allocation, parameter insertion, and string-keyed lookups can dominate the actual sandbox work and create Gen0 pressure proportional to helper-call count rather than module behavior.

## Suggested fix direction

Assign stable local slots during validation or plan construction and store frame values in a `SandboxValue[]` or pooled slot array. Keep a debug/name map for diagnostics, but let hot local reads/writes and parameter binding use integer indexes instead of allocating a dictionary per invocation.

## Benchmark/allocation test idea

Add interpreter benchmarks for helper calls in loops with 0, 2, and 20 parameters/locals across 1,000 to 100,000 invocations. Measure allocated bytes and variable access time, and assert steady-state helper invocation does not allocate a string-keyed dictionary for every frame.
