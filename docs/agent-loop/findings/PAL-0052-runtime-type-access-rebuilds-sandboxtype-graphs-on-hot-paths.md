---
id: PAL-0052
area: perf_alloc
status: open
priority: medium
title: Runtime type access rebuilds SandboxType graphs on hot paths
dedup_key: alloc/runtime/sandbox-type/rebuild-graphs-on-hot-path
created_at: 2026-06-13T06:56:21.4722503+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:56:21.4722503+00:00
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

# PAL-0052: Runtime type access rebuilds SandboxType graphs on hot paths

## Summary

Runtime hot paths rebuild `SandboxType` graphs instead of reusing stable type instances. Collection and opaque value `Type` getters call the allocating `SandboxType` factories, and compiled generated code emits calls to the same factories for parameter, return, and literal type constants during execution.

## Evidence

- `src/DotBoxd.Kernels/Sandbox/SandboxType.cs:43` through `src/DotBoxd.Kernels/Sandbox/SandboxType.cs:47` implement `Scalar`, `List`, and `Map` as `new SandboxType(...)` factory calls, with list/map arguments copied into read-only wrappers by the constructor.
- `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:93`, `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:113`, `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:122`, and `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:134` rebuild scalar/list/map type objects from value metadata every time `Type` is read.
- `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:27` compares `frame.Value.Type` for every visited value during recursive boundary validation, so nested collection validation allocates type objects while it walks values.
- `src/DotBoxd.Kernels/Sandbox/Values/SandboxValidatedValueShapeMeter.cs:211` performs the same `value.Type` comparison on binding-return validation and metering.
- `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs:124` and `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs:131` pass `list.Type` and `map.Type` into recursive validation on collection reads, and `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:303` and `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:310` do the same in compiled runtime collection reads.
- `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:32` through `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:34` expose `TypeScalar`, `TypeList`, and `TypeMap` helpers that also allocate through `SandboxType` factories.
- `src/DotBoxd.Kernels.Compiler/IlEmitterPrimitives.cs:55` through `src/DotBoxd.Kernels.Compiler/IlEmitterPrimitives.cs:72`, `src/DotBoxd.Kernels.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs:181`, `src/DotBoxd.Kernels.Compiler/Emitters/MethodEmitter.cs:279`, `src/DotBoxd.Kernels.Compiler/Emitters/CompiledTypeEmitter.cs:10` through `src/DotBoxd.Kernels.Compiler/Emitters/CompiledTypeEmitter.cs:31`, and `src/DotBoxd.Kernels.Compiler/Emitters/CompiledLiteralEmitter.cs:118`, `:132`, and `:133` emit runtime calls that rebuild type constants for entrypoint arguments, function returns, and collection literal/list/map helper calls.
- Existing `ALG-0008` covers recursive revalidation on collection reads, `ALG-0021` covers duplicated entrypoint traversal, `PAL-0045` and `PAL-0049` cover traversal-state allocation, and `PAL-0016` covers compile-time reflection while emitting helper calls. This finding is the separate heap churn from reconstructing stable `SandboxType` objects on runtime validation and compiled execution paths.

## Impact

Type identity is stable after validation, but read-heavy sandbox code and compiled entrypoints repeatedly allocate type records, argument arrays/wrappers, and nested type graphs just to compare against expected types. This adds Gen0 pressure to collection reads, binding-return validation, entrypoint argument binding, and compiled function return checks. The cost is visible after larger traversal fixes because the remaining expected-type checks should be cheap metadata comparisons rather than heap allocation.

## Suggested direction

Cache canonical `SandboxType` instances for known scalar types and for immutable list/map type metadata carried by values, parameters, and generated code. Store a precomputed `SandboxType` on `ListValue` and `MapValue`, return static instances for known scalar/opaque/URI types, and have compiled generated artifacts reference cached type fields or runtime singleton helpers instead of constructing the same type graph on every invocation. Preserve structural equality for public model compatibility, but make hot-path type access allocation-free.

## Benchmark/allocation test idea

Add allocation benchmarks for interpreted and compiled collection reads over scalar, list, and map values, plus a compiled entrypoint with scalar and nested collection parameters/returns executed 1, 1,000, and 100,000 times. Measure bytes allocated by expected-type checks and assert repeated `SandboxValue.Type` reads and generated `TypeScalar`/`TypeList`/`TypeMap` calls do not allocate new `SandboxType` graphs on the steady-state path.
