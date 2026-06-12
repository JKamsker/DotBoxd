---
id: PAL-0026
area: perf_alloc
status: open
priority: medium
title: Generated assembly verifier copies the full PE buffer before metadata reads
dedup_key: alloc/generated-verifier/pe-buffer-copy
created_at: 2026-06-12T22:27:03.2713832+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:27:03.2713832+00:00
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

# PAL-0026: Generated assembly verifier copies the full PE buffer before metadata reads

## Claim

Generated assembly verification hashes the input assembly from `ReadOnlyMemory<byte>` and then immediately copies the entire PE image into a new byte array before metadata verification. This full-buffer copy is avoidable because the verifier only needs a readable PE view over the provided immutable input memory.

## Evidence

- `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:18` computes the assembly hash directly from `assemblyBytes.Span`, proving the input can already be consumed without copying.
- `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:32` then constructs `new MemoryStream(assemblyBytes.ToArray(), writable: false)`, allocating and copying the complete assembly image before constructing `PEReader`.
- `VerifyMetadata` and its callees only read PE metadata and method bodies; they do not mutate or retain the copied array.
- Existing PAL-0007 covers repeated defensive copies in compiled artifact guard/materialization. This finding is separate: the generated verifier itself performs a full PE copy even when its caller already supplied a `ReadOnlyMemory<byte>` buffer.

## Impact

Compiled artifact verification sits on the compile/cache path. Large generated assemblies pay one full assembly-size allocation and copy before metadata walking begins, adding avoidable Gen0/LOH pressure and increasing verification latency on cache misses and regenerated artifacts.

## Better target

Use a non-copying PE input path, such as a read-only stream over `ReadOnlyMemory<byte>` or an immutable byte owner accepted by `PEReader`, so hashing and metadata verification share the same input buffer. Copy only when crossing an ownership boundary that actually requires a defensive copy.

## Benchmark/allocation test idea

Add a verifier allocation benchmark with 64 KB, 512 KB, and 2 MB generated assembly buffers. Assert `GeneratedAssemblyVerifier.VerifyAsync` does not allocate a second full-size PE buffer before metadata reads.
