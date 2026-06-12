---
id: ALG-0017
area: perf_algorithm
status: open
priority: medium
title: Compiled executable cache rehashes artifacts before cache hits
dedup_key: algorithm/compiled-executable-cache/full-artifact-validation-before-cache-hit
created_at: 2026-06-12T23:21:29.0889897+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T23:21:29.0889897+00:00
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

# ALG-0017: Compiled executable cache rehashes artifacts before cache hits

## Claim

`CompiledExecutableCache.GetAsync` validates the full `CompiledArtifact` before consulting the materialized executable cache. `CompiledArtifactGuard.ValidateExecutableEnvelope` hashes the full assembly image and rebuilds expected cache keys, so every compiled run pays artifact-size work even when `_entries` can serve an already materialized executable.

## Evidence

- `src/SafeIR.Hosting/Execution/SandboxHost.cs` routes compiled execution through `TryExecuteCompiledAsync`, which compiles an artifact and then calls `_compiledExecutables.GetAsync(...)` before `CompiledExecutionRunner.ExecuteAsync` can invoke the entrypoint.
- `src/SafeIR.Hosting/Execution/CompiledExecutableCache.cs` calls `CompiledArtifactGuard.ValidateExecutableEnvelope(artifact, plan, entrypoint)` before deriving the cache key and before `_entries.GetOrAdd(...)`, so the same validation runs for materialized-cache hits as well as misses.
- `src/SafeIR.Hosting/Execution/CompiledArtifactGuard.cs` implements `ValidateExecutableEnvelope` by calling `EnsureMatchesPlan` and `EnsureAssemblyBytesMatchHash`. `EnsureAssemblyBytesMatchHash` computes `SHA256.HashData(artifact.AssemblyBytesMemory.Span)` over the full DLL before comparing it with `artifact.AssemblyHash`.
- `EnsureMatchesPlan` calls `ExpectedOptimizationFlags`, which calls `CacheKeyBuilder.Build(...)` for both boxed and optimized variants before accepting the manifest cache key. `src/SafeIR.Compiler/CacheKeyBuilder.cs` builds a parts array, joins it into a string, encodes UTF-8, and hashes it on each call.
- Existing `PAL-0007` covered defensive assembly-byte copies inside this guard and is verified. Existing `PAL-0031` covers unbounded retention of materialized executables, and `ALG-0013` covers prepared-plan revalidation. This finding is separate: steady-state compiled executable cache hits still pay full artifact byte hashing and cache-key reconstruction before the cached delegate can be reused.

## Impact

A host that repeatedly executes the same compiled plan should amortize artifact verification and delegate materialization after the first successful materialization. Instead, every compiled dispatch still performs work proportional to the generated DLL size plus repeated cache-key hashing before sandbox code runs. For large generated modules or high-frequency plugin kernels, this fixed pre-entrypoint cost can dominate otherwise cheap compiled executions.

## Better target

Split compiled artifact validation into a cheap cache-hit identity check and a full miss/materialization validation. Cache hits should verify plan/manifest identity with precomputed expected cache key state and the cached assembly hash, then reuse the already validated materialized executable. Full byte hashing and verifier-envelope checks should remain inside the first materialization path for a cache key. Avoid rebuilding both optimized and boxed cache keys per dispatch by carrying the chosen optimization identity or caching expected keys per prepared plan and entrypoint.

## Benchmark/allocation test idea

Add a benchmark that executes the same prepared compiled entrypoint 1, 100, and 10,000 times with synthetic artifact sizes such as 64 KB, 512 KB, and 2 MB after the materialized executable cache is warm. Measure pre-entrypoint time and allocated bytes, and assert the cache-hit path no longer hashes the full assembly image or rebuilds cache-key strings per run.
