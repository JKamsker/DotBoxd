---
id: ALG-0008
area: perf_algorithm
status: open
priority: medium
title: Collection reads recursively revalidate entire values
dedup_key: algorithm/collections/read/full-value-revalidation
created_at: 2026-06-12T22:18:29.1886442+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:18:29.1886442+00:00
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

# ALG-0008: Collection reads recursively revalidate entire values

## Claim

Read-only list and map operations recursively validate the entire collection value before performing simple count/get/contains lookups, so repeated reads over an already-typed collection become O(operation-count * collection-size) in both interpreted and compiled execution.

## Evidence

- `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs:21` and `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs:28` route `list.count` and `list.get` through `AsList`.
- `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs:66` and `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs:74` route `map.containsKey` and `map.get` through `AsMap`.
- `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs:114` through `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs:125` implement `AsList`/`AsMap` by calling `SandboxValueValidator.RequireType` on the whole list or map value.
- Compiled mode mirrors the same pattern: `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:154`, `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:161`, `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:196`, and `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:204` call `AsList`/`AsMap` for read-only collection operations.
- `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:260` through `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:270` also implement `AsList`/`AsMap` by recursively validating the complete collection.
- `SandboxValueValidator.RequireType` uses an explicit stack and walks every list element or map key/value pair at `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:14` through `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:46`, with list/map child pushes at `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:63` through `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:68` and `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:86` through `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:92`.
- Collection values snapshot their contents and carry item/key/value type metadata when constructed at `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:116` through `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs:134`, and construction/return paths already charge full shape through `ResourceMeter.ChargeValue` at `src/DotBoxd.Kernels/Model/Resources.cs:99` through `src/DotBoxd.Kernels/Model/Resources.cs:102`.
- This is distinct from `ALG-0002`, which covers whole-container copies during mutation, and from `PAL-0003`, which covered avoidable map traversal buffers inside validation/metering. Here the problem is that read-only operations repeatedly rewalk an already-created collection even when the actual operation is `Count`, index lookup, `ContainsKey`, or `TryGetValue`.

## Impact

A loop that repeatedly reads from a 10,000-element list or map pays a recursive 10,000-element validation walk for every read before reaching the O(1) list count/index or dictionary lookup. That makes common read-heavy sandbox code scale with collection size times read count in both interpreter and compiled runtimes, and also burns fuel/deadline checks in validator traversal without adding new safety for values that were already snapshotted and typed at construction or boundary validation.

## Better target

Validate collection contents when values cross trust boundaries or are constructed, then let runtime collection helpers trust the `ListValue`/`MapValue` type metadata for internal read operations. If defensive checks are still needed, prefer a cheap invariant marker or shallow type check in `AsList`/`AsMap`, leaving full recursive validation to boundary APIs such as entrypoint input and binding returns.

## Benchmark idea

Add interpreted and compiled benchmarks that build a fixed list/map of 100, 1,000, and 10,000 scalar elements, then perform 10,000 `list.count`, `list.get`, `map.containsKey`, and `map.get` operations without mutation. Measure elapsed time, allocated bytes, and fuel/deadline-check overhead before and after removing full recursive validation from read-only collection helpers.
