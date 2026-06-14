---
id: PAL-0005
area: perf_alloc
status: fixed_pending_verification
priority: low
title: Generated stack verifier allocates while parsing call signatures
dedup_key: alloc/verifier/generated-stack/call-signature-split-per-call
created_at: 2026-06-12T21:01:52.0467908+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T21:59:52.6736906+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:58:14.8525073+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:59:52.6736906+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# PAL-0005: Generated stack verifier allocates while parsing call signatures

## Claim

Generated stack verification reparses member signature strings and allocates substrings/split arrays for every call instruction instead of using decoded call metadata or cached signature shapes.

## Evidence

- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedStackVerifier.cs:23` walks the generated method control-flow queue and calls `OutputDepth` for each reachable instruction.
- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedStackVerifier.cs:89` routes every `Call`, `Callvirt`, and `Newobj` instruction through `CallDelta(instruction.CalledMember)`.
- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedStackVerifier.cs:111` computes stack delta from the string signature by calling `ParameterCount(signature)` and `ReturnsVoid(signature)`.
- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedStackVerifier.cs:121` finds delimiters with `IndexOf`/`LastIndexOf` for each call signature.
- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedStackVerifier.cs:128` slices the parameter substring and calls `Split(',')`, allocating a substring plus an array on every call instruction with parameters.
- Existing tests exercise verifier correctness, but the benchmark project has no verifier allocation benchmark for methods with many runtime/helper calls.

## Impact

Generated methods can contain many runtime calls for metering, collection helpers, binding dispatch, and value conversion. Verifying such methods pays repeated parsing and allocation for signatures that are already known when `GeneratedInstruction` is decoded. This adds avoidable allocation churn to compile/cache validation paths.

## Better target

Decode and store parameter count and return-void shape once when reading member signatures, or cache parsed signature shape by `CalledMember` string for the verification pass. `StackDelta` should use integer metadata rather than reparsing strings per instruction.

## Benchmark idea

Add a BenchmarkDotNet verifier allocation benchmark that generates methods with 100, 1,000, and 10,000 call instructions to a small set of repeated signatures. Measure allocated bytes in `GeneratedAssemblyVerifier` and specifically assert that repeated call-signature verification does not allocate per instruction.
