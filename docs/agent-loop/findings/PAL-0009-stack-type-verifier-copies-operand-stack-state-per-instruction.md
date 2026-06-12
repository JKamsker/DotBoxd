---
id: PAL-0009
area: perf_alloc
status: open
priority: medium
title: Stack type verifier copies operand stack state per instruction
dedup_key: alloc/verifier/generated-stack-type/state-copy-per-instruction
created_at: 2026-06-12T22:02:46.8191964+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:02:46.8191964+00:00
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

# PAL-0009: Stack type verifier copies operand stack state per instruction

## Claim

Generated stack-type verification copies the whole symbolic operand stack for every reachable instruction and again when storing new successor states, so verifier cost grows with instruction count times stack depth even when call-signature parsing is already cached.

## Evidence

- `src/SafeIR.Verifier/Generated/GeneratedStackTypeVerifier.cs` starts each transfer with `var stack = input.ToList()`, allocating and copying the incoming stack state for every processed instruction.
- The same verifier stores a first-seen successor state with `stacks[successor] = output.ToArray()`, adding another full stack copy per discovered control-flow state.
- Merge checks compare the stored and new stack states with `SequenceEqual`, so branch-heavy methods repeatedly scan stack-state strings as well as allocate lists/arrays.
- This is distinct from `PAL-0005`: call-signature delta parsing is now cached in `GeneratedStackVerifier`, but stack-type verification still represents state as copied `IReadOnlyList<string>` snapshots.
- Current `benchmarks/SafeIR.Benchmarks/Verifier/GeneratedVerifierCallBenchmarks.cs` stresses repeated runtime calls, but it does not vary symbolic stack depth or branch merge count for `GeneratedStackTypeVerifier` state copying.

## Impact

Large generated methods with deep expression trees, value-array construction, or branch joins can allocate one list plus one or more arrays per reachable instruction. Verification sits on compile/cache-hit materialization paths, so this creates avoidable Gen0 pressure and can make verification latency scale with transient stack depth instead of just IL size.

## Better target

Use a compact stack-state representation with structural sharing, pooled buffers, or immutable state nodes keyed by `(previous, pushed/popped type)` so transfer can avoid cloning the whole stack on every instruction. Merge checks should compare cached state identity or compact hashes before falling back to element comparison.

## Benchmark/allocation test idea

Add a BenchmarkDotNet verifier benchmark that emits generated methods with 100, 1,000, and 10,000 instructions while varying max operand stack depth and branch merge fan-in. Measure allocated bytes and time for `GeneratedAssemblyVerifier`, and include a regression allocation test asserting stack-type verification does not allocate O(instructions * stackDepth) list/array snapshots.
