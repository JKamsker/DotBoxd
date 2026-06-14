---
id: ALG-0021
area: perf_algorithm
status: open
priority: medium
title: Entrypoint input charging traverses values again during argument binding
dedup_key: algorithm/entrypoint-input/boundary/charge-then-validate-traversal
created_at: 2026-06-13T06:34:41.4404425+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:34:41.4404425+00:00
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

# ALG-0021: Entrypoint input charging traverses values again during argument binding

## Claim

Entrypoint input handling traverses large values once for quota charging and then again for argument validation/binding. Both interpreted and compiled execution charge the full input shape before `EntrypointBinder` recursively validates the same value or each extracted argument against the entrypoint parameter types.

## Evidence

- `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs` calls `_context.ChargeValue(input)` in `ExecuteEntrypointAsync`, then immediately calls `EntrypointBinder.BindArguments(function, input)`.
- `src/DotBoxd.Hosting/Execution/CompiledExecutionRunner.cs` calls `context.ChargeValue(input)` before invoking the compiled entrypoint delegate.
- `src/DotBoxd.Kernels.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs` emits `CompiledRuntime.ValidateEntrypointInput` and one `CompiledRuntime.GetInputArgument` call per parameter in the generated `Execute` method.
- `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs` forwards those helpers to `EntrypointBinder.ValidateInputShape` and `EntrypointBinder.GetArgument`.
- `src/DotBoxd.Kernels/Model/EntrypointBinder.cs` validates each argument through `SandboxValueValidator.RequireType`, which recursively walks list/map values.
- Existing `PAL-0018` covered repeated traversal for binding returns, and `ALG-0010` covered compiled binding argument revalidation. This finding is the entrypoint input boundary that runs for every sandbox execution.

## Impact

A single-parameter entrypoint receiving a 10,000-element list or map is traversed once by `ResourceMeter.ChargeValue` and again by `SandboxValueValidator.RequireType`. Multi-parameter tuple inputs are charged as a whole and then each nested argument is validated separately. Repeated executions of the same prepared plan therefore pay duplicate O(input-size) traversal and traversal-state allocation before user code starts.

## Better target

Use `SandboxValidatedValueShapeMeter` or an equivalent combined boundary pass to validate scalar/list/map invariants and compute resource shape once. For multi-parameter inputs, bind arguments while collecting or reusing per-argument validated shape data so quota charging and type checking share traversal work.

## Benchmark idea

Add interpreted and compiled execution benchmarks with one large list/map input and with multi-parameter tuple inputs containing large nested values at 100, 1,000, and 10,000 elements. Measure pre-entrypoint elapsed time and allocations, and assert entrypoint input boundary work is one validated shape traversal rather than charge plus validation passes.
