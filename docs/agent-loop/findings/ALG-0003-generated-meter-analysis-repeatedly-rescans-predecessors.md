---
id: ALG-0003
area: perf_algorithm
status: verified
priority: medium
title: Generated meter analysis repeatedly rescans predecessors
dedup_key: algorithm/verifier/generated-meter/predecessor-rescan
created_at: 2026-06-12T21:00:41.3695436+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T21:33:05.2174809+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:28:21.7534700+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:30:14.9551826+00:00
fixed_commit: 
verified_by: independent-verifier
verified_at: 2026-06-12T21:33:05.2174809+00:00
verified_commit: 
duplicate_of: 
---

# ALG-0003: Generated meter analysis repeatedly rescans predecessors

## Claim

Generated method metering analysis repeatedly scans the full instruction list to rediscover predecessor and instruction-index information, so verification cost can grow quadratically with method size/control-flow density.

## Evidence

- `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodMeterAnalyzer.cs:64` materializes `Predecessors(analysis, instruction.Offset).ToArray()` for an immediate-meter check.
- `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodMeterAnalyzer.cs:126` scans `analysis.Instructions` from the beginning to find the current instruction index every time `HasPositiveImmediateMeterAmount(analysis, instruction)` is called.
- `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodMeterAnalyzer.cs:136` implements `Predecessors` as `analysis.Instructions.Where(...)`, and each candidate asks `GeneratedMethodFlowAnalyzer.Successors(...).Contains(offset)`.
- `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodMeterAnalyzer.cs:21` and `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodMeterAnalyzer.cs:83` call successor traversal inside queue-based graph walks, so the immediate-meter helper can be reached for many instructions during both unmetered-work and sparse-meter checks.
- Existing benchmarks are under `benchmarks/DotBoxd.Kernels.Benchmarks/Ipc`; there is no verifier benchmark that scales generated method size, branch count, or meter density.

## Impact

A generated method with many branches or meter calls pays repeated O(instruction-count) scans while already walking the control-flow graph. This can make artifact verification latency grow faster than the IL size for large generated kernels, exactly on the prepare/cache path where compilation and verification are expected to be predictable.

## Better target

Precompute instruction index by offset and predecessor lists once as part of `GeneratedMethodFlow`, or during analyzer setup. Immediate-meter checks should be O(1) for index lookup and O(in-degree) for predecessor inspection instead of O(instructions * successors) per check.

## Benchmark idea

Add a BenchmarkDotNet verifier benchmark that constructs generated methods with 100, 1,000, and 10,000 instructions plus configurable branch fan-out and meter frequency. Measure elapsed time and allocations for `GeneratedAssemblyVerifier` or the generated method flow/meter analyzers before and after predecessor/index caching.
