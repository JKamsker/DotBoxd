---
id: ALG-0002
area: perf_algorithm
status: open
priority: medium
title: Collection mutations copy whole containers per update
dedup_key: algorithm/collections/mutation/whole-container-copy
created_at: 2026-06-12T20:36:50.6172810+00:00
created_by: perf-reviewer
created_commit: 
updated_at: 2026-06-12T21:22:11.2109550+00:00
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

# ALG-0002: Collection mutations copy whole containers per update

## Claim

List and map mutation APIs copy the entire collection on every single update in both interpreted and compiled execution, so loop-built collections are O(n^2) time and allocation.

## Evidence

- Interpreted `list.add` copies all existing values with `source.Values.ToList()` at `src/SafeIR.Interpreter/Internal/CollectionOperations.cs:47`, then wraps the new list at `src/SafeIR.Interpreter/Internal/CollectionOperations.cs:49`.
- Interpreted `map.set` and `map.remove` copy the whole dictionary at `src/SafeIR.Interpreter/Internal/CollectionOperations.cs:95` and `src/SafeIR.Interpreter/Internal/CollectionOperations.cs:109`.
- Compiled mode mirrors the same behavior: `CompiledRuntime.ListAdd` copies with `source.Values.ToList()` at `src/SafeIR.Runtime/CompiledRuntime.cs:184`, while `MapSet` and `MapRemove` copy whole dictionaries at `src/SafeIR.Runtime/CompiledRuntime.cs:225` and `src/SafeIR.Runtime/CompiledRuntime.cs:239`.
- Existing collection tests validate single-operation behavior and quota accounting, for example `tests/SafeIR.Tests/Misc06/SafeCollectionTests.cs:131` and `tests/SafeIR.Tests/Misc07/SafeMapCollectionTests.cs:258`, but there is no throughput/allocation benchmark for repeated growth.
- The benchmark project currently only contains IPC benchmarks under `benchmarks/SafeIR.Benchmarks/Ipc`, leaving collection growth unmeasured.

## Impact

A module that builds a list/map incrementally in a loop pays 1 + 2 + ... + n copies. At 10,000 appended items this becomes tens of millions of copied entries and proportional allocation churn in both interpreter and compiled modes. This is algorithmic, not a micro-allocation issue.

## Better target

If immutable values are required, consider a builder/batch primitive, persistent data structure with structural sharing, or compiler/runtime recognition of local collection construction before publishing the final immutable value. Target repeated append/set construction at O(n) or O(n log n) rather than O(n^2).

## Benchmark idea

Add BenchmarkDotNet cases for interpreted and compiled modules that build list/map values of 100, 1,000, and 10,000 items. Measure elapsed time, Gen0/allocated bytes, and compare against `list.of` or a proposed batch builder baseline.
