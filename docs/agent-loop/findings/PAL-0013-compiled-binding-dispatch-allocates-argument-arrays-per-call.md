---
id: PAL-0013
area: perf_alloc
status: open
priority: medium
title: Compiled binding dispatch allocates argument arrays per call
dedup_key: alloc/compiled-runtime/binding-dispatch/argument-array-per-call
created_at: 2026-06-12T22:07:29.9269415+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:07:29.9269415+00:00
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

# PAL-0013: Compiled binding dispatch allocates argument arrays per call

## Claim

Compiled binding dispatch allocates a fresh `SandboxValue[]` for every compiled binding call, even for small pure/intrinsic bindings inside hot loops.

## Evidence

- `src/DotBoxd.Kernels.Compiler/Emitters/BindingCallEmitter.cs:22` emits `ValueArrayEmitter.Emit(...)` for compiled pure binding calls before calling `CompiledRuntime.CallBinding`.
- `src/DotBoxd.Kernels.Compiler/Emitters/ValueArrayEmitter.cs:16` emits a call to `CompiledRuntime.CreateValueArray` for every call site execution, then fills it element-by-element.
- `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:244` exposes `CallBinding(SandboxContext, string, SandboxValue[] args)`, and `src/DotBoxd.Kernels.Runtime/CompiledBindingDispatcher.cs:7` requires that array-shaped argument payload.
- `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:252` charges and allocates the argument array in `CreateValueArray`, then returns `new SandboxValue[count]`.
- Existing benchmarks cover verifier runtime-call validation (`benchmarks/DotBoxd.Kernels.Benchmarks/Verifier/GeneratedVerifierCallBenchmarks.cs`) and interpreter expression execution, but there is no compiled binding dispatch benchmark that loops over pure host-facade/intrinsic bindings and measures per-call allocations.

## Impact

A compiled module that calls a pure binding in a loop pays one managed array allocation per binding invocation before the binding descriptor can validate and dispatch. This undercuts compiled-mode hot paths where the actual binding may be cheap, such as math/string/plugin-message facade calls, and it is separate from collection mutation copying or verifier stack-state allocation.

## Better target

Provide specialized compiled dispatch helpers for common arities, pass arguments via `ReadOnlySpan<SandboxValue>`/stackalloc where safe, or generate direct typed calls for known pure intrinsic bindings. The target should avoid heap allocation for zero-, one-, and two-argument compiled binding calls.

## Benchmark/allocation test idea

Add a BenchmarkDotNet compiled-runtime benchmark that executes modules with 1, 10, and 1,000 repeated pure binding calls per run, varying binding arity from 0 to 4. Measure allocated bytes in compiled mode and add an allocation regression test asserting common arities do not allocate a `SandboxValue[]` per dispatch.
