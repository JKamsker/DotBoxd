---
id: PAL-0025
area: perf_alloc
status: open
priority: medium
title: Generated method flow analysis allocates successor arrays per instruction
dedup_key: alloc/generated-verifier/flow-successor-arrays
created_at: 2026-06-12T22:27:02.0247378+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:27:02.0247378+00:00
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

# PAL-0025: Generated method flow analysis allocates successor arrays per instruction

## Claim

Generated method flow analysis allocates per-instruction successor arrays and predecessor arrays, then later recomputes successors from the same instruction list during reachability and cycle checks. The control-flow graph is constructed eagerly for every generated method body verified, even though most IL instructions have zero or one successor and later traversals do not reuse the allocated successor map consistently.

## Evidence

- `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodFlowAnalyzer.cs:11` builds `byOffset` with `instructions.ToDictionary(...)` for every method analysis.
- `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodFlowAnalyzer.cs:45` creates a `Dictionary<int, IReadOnlyList<int>>` sized to every instruction.
- `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodFlowAnalyzer.cs:48` calls `Successors(...).ToArray()` once per instruction, allocating an array even for the common fallthrough-only case.
- `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodFlowAnalyzer.cs:73` through `src/DotBoxd.Kernels.Verifier/Generated/Methods/GeneratedMethodFlowAnalyzer.cs:75` converts predecessor lists with another `ToDictionary` and `ToArray` pass.
- `ReachableStates` still calls `Successors(instructions, byOffset, instruction)` directly while walking the queue, so it repeats successor discovery instead of using the already materialized `successorsByOffset`.
- `HasUnmeteredCycle` materializes `reachableOffsets.ToHashSet()` and `Visit` again calls `Successors(...)`, adding another traversal/allocation layer over the same control-flow data.
- Existing ALG-0003 covers meter-analysis predecessor rescans; this is distinct because the shared flow analyzer eagerly allocates successor/predecessor graph containers and then recomputes successors in later generic flow traversals.

## Impact

Verifier cost scales with generated method instruction count. Large generated functions pay O(instruction-count) dictionary entries plus many tiny successor arrays before stack, shape, and meter checks run. Because most generated IL uses linear fallthrough, these arrays mostly wrap one integer and create avoidable Gen0 pressure on every compile/verify path.

## Better target

Represent successors with a compact struct or reuse a single materialized successor map across `ReachableStates`, cycle detection, stack verification, and meter analysis. Avoid `IEnumerable<int>`/`yield` plus `ToArray()` for the common zero/one/two successor cases, and store predecessor data only when a downstream verifier actually needs it.

## Benchmark/allocation test idea

Add a verifier allocation benchmark for generated methods with 1k, 10k, and 50k mostly-linear instructions plus a branch-heavy variant. Assert flow analysis allocations scale with required graph edges rather than allocating one array per instruction.
