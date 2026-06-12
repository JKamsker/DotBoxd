---
id: PAL-0007
area: perf_alloc
status: fixed_pending_verification
priority: medium
title: Compiled artifact guard copies assembly bytes repeatedly
dedup_key: alloc/compiled-artifact/assembly-bytes/repeated-defensive-copies
created_at: 2026-06-12T21:03:30.3274123+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T21:57:10.4412769+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:54:58.3645782+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:57:10.4412769+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# PAL-0007: Compiled artifact guard copies assembly bytes repeatedly

## Claim

Compiled artifact validation and materialization make multiple full copies of the assembly byte array because `CompiledArtifact.AssemblyBytes` defensively copies on every getter and several hot guard paths read it repeatedly.

## Evidence

- `src/SafeIR.Compiler/CompilerContracts.cs:87` implements `CompiledArtifact.AssemblyBytes` as `get => _assemblyBytes.ToArray()`, so every property read copies the full assembly image.
- `src/SafeIR.Hosting/Execution/CompiledArtifactGuard.cs:28` then calls `artifact.AssemblyBytes.ToArray()`, copying once in the getter and again in the explicit `ToArray()`.
- `src/SafeIR.Hosting/Execution/CompiledArtifactGuard.cs:67` and `src/SafeIR.Hosting/Execution/CompiledArtifactGuard.cs:72` read `artifact.AssemblyBytes.Length` during envelope validation, which still copies the entire byte array just to check length.
- `src/SafeIR.Hosting/Execution/CompiledArtifactGuard.cs:113` hashes `artifact.AssemblyBytes`, causing another full defensive copy before hashing.
- `src/SafeIR.Hosting/Execution/CompiledArtifactGuard.cs:39` constructs a new `CompiledArtifact` with `assemblyBytes`, and `src/SafeIR.Compiler/CompilerContracts.cs:88` copies again in the init setter.
- Existing compiled cache tests cover materialization behavior, but there is no allocation benchmark for materializing large compiled artifacts.

## Impact

Loaded compiled artifacts can be tens or hundreds of kilobytes. Each materialization/guard pass can copy the same assembly image several times before verification and loading, increasing Gen0/LOH pressure and making cache hits more allocation-heavy than necessary.

## Better target

Expose assembly bytes as `ReadOnlyMemory<byte>` or an immutable owner internally, and provide a single explicit copy only at trust boundaries that require ownership transfer. Length and hash checks should operate on a span/memory view without copying.

## Benchmark idea

Add a BenchmarkDotNet allocation benchmark that materializes synthetic compiled artifacts with 64 KB, 512 KB, and 2 MB assembly images. Measure allocated bytes in `CompiledExecutableCache.GetAsync`/`CompiledArtifactGuard.MaterializeExecutableAsync`, with assertions that validation does not copy the assembly image more than necessary.
