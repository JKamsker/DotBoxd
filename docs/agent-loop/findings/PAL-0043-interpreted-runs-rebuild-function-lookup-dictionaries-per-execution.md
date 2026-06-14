---
id: PAL-0043
area: perf_alloc
status: open
priority: medium
title: Interpreted runs rebuild function lookup dictionaries per execution
dedup_key: alloc/interpreter/execution/function-lookup-dictionary-per-run
created_at: 2026-06-13T06:34:43.9571380+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:34:43.9571380+00:00
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

# PAL-0043: Interpreted runs rebuild function lookup dictionaries per execution

## Claim

Every interpreted execution rebuilds a module-wide function lookup dictionary from `plan.Module.Functions`, even when the same prepared `ExecutionPlan` is executed repeatedly and the function set is immutable.

## Evidence

- `src/DotBoxd.Kernels.Interpreter/SandboxInterpreter.cs` constructs a new `InterpreterEvaluator(plan, context, options)` for each `ExecuteAsync` call.
- `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs` builds `_functions = plan.Module.Functions.ToDictionary(f => f.Id, StringComparer.Ordinal)` in the evaluator constructor.
- Helper-call dispatch then uses that per-run dictionary through `TryGetFunction`, but the mapping is stable for the lifetime of the prepared plan.
- Existing `ALG-0005` covered binding-reference collection dictionaries during validation/dispatch and is not the interpreted execution function lookup table.

## Impact

Hosts that repeatedly execute one prepared plan in interpreted mode pay O(function-count) dictionary allocation and hashing before each run, even if the entrypoint is cheap and only touches a small subset of functions. Generated/plugin modules can contain many helper functions, making per-run setup scale with module size rather than executed work.

## Suggested fix direction

Store an immutable function lookup on `ExecutionPlan` or a prepared interpreter plan, or let the interpreter cache the dictionary by plan identity with bounded lifetime. Per-run state should contain only run-specific context/audit/budget data, not a rebuilt module index.

## Benchmark/allocation test idea

Add an interpreted execution benchmark that prepares modules with 10, 100, 1,000, and 10,000 functions but a cheap entrypoint, then executes the same plan 1, 100, and 10,000 times. Measure setup allocations and assert repeated interpreted runs reuse the function lookup instead of rebuilding it per execution.
