---
id: ALG-0020
area: perf_algorithm
status: open
priority: medium
title: Persistent compiled cache re-verifies cached assemblies on every read
dedup_key: algorithm/compiler-cache/read/reverify-cached-assembly
created_at: 2026-06-13T06:34:38.9881315+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:34:38.9881315+00:00
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

# ALG-0020: Persistent compiled cache re-verifies cached assemblies on every read

## Claim

`PersistentCompiledArtifactCache.TryReadCoreAsync` runs the generated assembly verifier on every successful persistent-cache read after it has already validated the manifest, cached verification record, and cache-origin proof. The first host materialization path then verifies the same loaded-assembly artifact again, so a persistent cache hit still pays full PE/metadata verification work instead of using the signed cache record as the cached verification proof.

## Evidence

- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs` reads `manifest.json`, validates `verification.json`, reads `module.dll`, and calls `PersistentCompiledArtifactCacheOrigin.ValidateProofAsync` inside `TryReadCoreAsync`.
- The same method then calls `verifier.VerifyAsync(assemblyBytes, manifest, policy.WithExpectedManifest(...))` before returning `CompiledCacheStatus.Hit`.
- `src/DotBoxd.Hosting/Execution/CompiledArtifactGuard.cs` calls `Verifier.VerifyAsync(...)` again inside `MaterializeExecutableAsync` before loading a `LoadedAssembly` artifact.
- Existing `ALG-0017` covers host-local `CompiledExecutableCache` validating artifacts before in-memory materialized-cache hits. This finding is separate: the persistent disk-cache hit path re-runs verifier work before an artifact reaches the host-local executable cache.

## Impact

A persistent cache hit should avoid compile and verifier latency for known-good artifacts. Today it still performs verifier work proportional to assembly size, method count, metadata tables, and control-flow complexity on every read, then repeats that verification on first materialization. Long-lived hosts that restart, CI jobs with warm caches, and plugin servers that load many cached kernels pay avoidable cold-start and cache-hit latency.

## Better target

After validating the cache key, manifest identity, verifier version, cached verification result, and host-bound origin proof, treat the cached verification record as the artifact's proof for the read path. Keep fail-closed verification when writing entries, when the verifier/runtime facade version changes, or when proof/manifest validation fails. If defense-in-depth still requires periodic verifier sampling, make it explicit and outside the steady-state hit path.

## Benchmark idea

Add a persistent-cache benchmark that seeds verified artifacts with 64 KB, 512 KB, and 2 MB generated assemblies, then measures 1, 100, and 1,000 `TryReadAsync` cache hits. Track elapsed time and allocations, and assert steady-state hits do not run full generated assembly verification after cache-origin proof validation succeeds.
