---
id: PAL-0041
area: perf_alloc
status: open
priority: medium
title: Untyped SandboxValue.FromList snapshots values twice
dedup_key: alloc/sandbox-value/list/fromlist-double-snapshot
created_at: 2026-06-13T06:34:40.2134580+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:34:40.2134580+00:00
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

# PAL-0041: Untyped SandboxValue.FromList snapshots values twice

## Claim

The untyped `SandboxValue.FromList(IReadOnlyList<SandboxValue>)` factory snapshots the input once to infer the item type and then `ListValue` snapshots that snapshot again, allocating two arrays/read-only wrappers for one immutable list value.

## Evidence

- `src/DotBoxd.Kernels/Sandbox/SandboxValue.cs` implements the untyped `FromList` overload by calling `ModelCopy.List(values)` before constructing `new ListValue(...)`.
- `ListValue` stores its `Values` through `ModelCopy.List(Values)`, so the constructor defensively copies the already-copied snapshot.
- `src/DotBoxd.Kernels/Model/ModelCopy.cs` implements `List<T>` as `values.ToArray()` plus `ReadOnlyCollection<T>`.
- Hot call sites include `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs` `ListOf`, `src/DotBoxd.Kernels.Interpreter/Internal/CollectionOperations.cs` `BuildList`, and public host/test input construction through `SandboxValue.FromList(...)`.
- Existing `PAL-0040` covers plugin kernel input using the typed `FromList(values, itemType)` overload after building a working array. This finding is the broader untyped factory double-snapshot that remains outside plugin input construction.

## Impact

Any list created without an explicit item type pays two full copies proportional to element count before runtime shape charging begins. Loop-built or generated `list.of` workloads, host-created entrypoint inputs, and compiled `ListOf` calls allocate extra Gen0 memory and duplicate copy bandwidth for large lists even when the caller already provides an exact-size array.

## Suggested fix direction

Infer the item type from the original list and construct `ListValue` with a single owned/internal snapshot, or add an internal constructor/factory that accepts an already-snapshotted exact-size array. Keep defensive copying for public mutable inputs, but ensure the public untyped factory performs only one snapshot.

## Benchmark/allocation test idea

Add allocation benchmarks for `SandboxValue.FromList` and interpreted/compiled `list.of` with 0, 1, 100, 1,000, and 10,000 elements. Assert the untyped factory allocates one backing array/wrapper rather than copying the same element sequence twice.
