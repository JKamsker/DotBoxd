---
id: PAL-0044
area: perf_alloc
status: open
priority: medium
title: Compiled collection literals allocate value arrays per execution
dedup_key: alloc/compiled-runtime/collection-literals/value-array-per-execution
created_at: 2026-06-13T06:34:45.2087484+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:34:45.2087484+00:00
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

# PAL-0044: Compiled collection literals allocate value arrays per execution

## Claim

Compiled collection literals allocate fresh `SandboxValue[]` buffers every time the literal expression executes. Map literals also rebuild a temporary dictionary from those arrays on every evaluation, even though the literal graph is immutable module data.

## Evidence

- `src/DotBoxd.Kernels.Compiler/Emitters/CompiledLiteralEmitter.cs` emits collection literals through `EmitListLiteral` and `EmitMapLiteral`, both of which call `EmitValueArray` for literal elements.
- `EmitValueArray` emits a runtime call to `CompiledRuntime.CreateLiteralValueArray` and fills the returned array element by element in the generated method body.
- `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs` forwards `CreateLiteralValueArray` to `CompiledLiteralRuntime.CreateValueArray`.
- `src/DotBoxd.Kernels.Runtime/Compiled/CompiledLiteralRuntime.cs` implements `CreateValueArray` as `new SandboxValue[count]`, and `MapLiteralValue` then builds a `Dictionary<SandboxValue, SandboxValue>` from the key/value arrays.
- Existing `PAL-0013` covers compiled binding argument arrays per binding dispatch. This finding is about compiled literal constants allocating value buffers per literal evaluation.

## Impact

Programmatic DotBoxd.Kernels and generated modules can contain list/map literals that are evaluated inside loops or frequently executed entrypoints. Compiled mode currently rebuilds the same literal arrays and map dictionaries per execution instead of reusing artifact-level immutable literal data, so allocation scales with literal size times execution count even when the literal contents never change.

## Suggested fix direction

Hoist immutable collection literal graphs into generated static fields or a per-artifact literal table, with safe lazy initialization and precomputed shape metadata. Runtime execution should charge quota for the literal value but avoid rebuilding the `SandboxValue[]` or dictionary storage for constants on every evaluation.

## Benchmark/allocation test idea

Add compiled-mode benchmarks for entrypoints that return or repeatedly evaluate list/map literals with 1, 100, 1,000, and 10,000 elements. Measure allocated bytes per execution after materialization is warm, and assert collection literal evaluation does not allocate fresh value arrays or map dictionaries for immutable literal data.
