---
id: COR-0048
area: correctness
status: verified
priority: high
title: Persistent compiled cache accepts verifier-safe artifacts without compiler-origin proof
dedup_key: security/compiled-cache/artifact-origin/semantic-tamper-evidence-missing
created_at: 2026-06-12T23:19:13.0618379+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-13T00:21:26.1933797+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:55:21.3523868+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:03:02.7757501+00:00
fixed_commit: pending
verified_by: codex-verifier
verified_at: 2026-06-13T00:21:26.1933797+00:00
verified_commit: 24668a7
duplicate_of: 
---

# COR-0048: Persistent compiled cache accepts verifier-safe artifacts without compiler-origin proof

## Claim

The persistent compiled cache treats a cache hit as trusted when the manifest/cache-key hashes match the current plan and the DLL passes the generated assembly verifier. The verifier proves the assembly is sandbox-shaped, but it does not prove the DLL was emitted by DotBoxd.Kernels's compiler for the module hash recorded in the manifest. A cache writer can replace a valid entry with a different verifier-safe assembly, update `manifest.json` and `verification.json` to the new assembly hash, and have the host execute semantics that do not correspond to the sealed module.

## Evidence

- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:97` reads the cached `manifest.json`, and `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:99` validates only manifest identity fields against the cache key, plan, entrypoint, and verifier policy.
- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:104` validates cached verification metadata, then `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:108` re-runs `GeneratedAssemblyVerifier.VerifyAsync` over the cached DLL with expected manifest identity.
- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:119` returns `CompiledCacheStatus.Hit` with the verified bytes. There is no host-secret signature, compiler-origin proof, or semantic equivalence check that binds the DLL body to `plan.ModuleHash` beyond the attacker-controlled manifest fields and assembly hash.
- `src/DotBoxd.Kernels.Compiler/Internal/PersistentCompiledArtifactCacheValidator.cs:16` through `src/DotBoxd.Kernels.Compiler/Internal/PersistentCompiledArtifactCacheValidator.cs:49` checks cache key, plan hashes, verifier/runtime versions, optimization flags, and `verification.AssemblyHash == manifest.AssemblyHash`, but it does not verify a signature over the DLL and manifest produced by the host/compiler.
- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.cs:19` through `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.cs:24` ensures the manifest assembly hash matches the bytes, then verifies metadata/IL safety. That safety gate does not reconstruct the source module semantics or prove the emitted IL corresponds to the hashed module.
- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.TempValidation.cs:21` through `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.TempValidation.cs:30` applies the same identity/hash checks when publishing a temp entry, but still does not add an origin signature that a later cache read can require.

This is distinct from `COR-0032`, which covers direct verifier calls without expected manifest identity, and from the mutable model findings. Here the hosted cache path supplies expected manifest identity and still lacks tamper evidence that the verified assembly was compiler-produced for that module.

## Risk

The compiled cache is an execution trust boundary. If a cache directory is shared, restored from CI, copied between hosts, or writable by a same-user process, a forged entry can preserve the current cache key, module hash, policy hash, binding manifest hash, and repository-visible metadata while changing the pure computation that compiled mode runs. The replacement assembly can remain inside the verifier sandbox, so this is not a CLR escape, but it can corrupt authorization decisions, scoring, filtering, or plugin predicates that rely on DotBoxd.Kernels module semantics. Interpreted mode would execute the sealed module; compiled cache-hit mode can execute attacker-chosen sandbox-safe semantics.

## Suggested acceptance tests

- Create a valid compiled cache entry for a pure module whose `main` returns `1`.
- Replace `module.dll` with a verifier-safe generated assembly for the same entrypoint shape that returns `2`, update `manifest.json` and `verification.json` assembly hashes while preserving the cache key and plan identity fields, then execute with compiled cache enabled.
- The fixed behavior should quarantine the entry instead of returning a cache hit. The test should fail if compiled execution returns `2` for the original module.

## Expected behavior

A persistent compiled cache hit should require tamper evidence that only the current host/compiler can produce for the tuple of plan identity, entrypoint, verifier/runtime identity, optimization flags, and assembly bytes. Hashes in attacker-editable JSON are not enough to prove compiler origin or semantic binding.

## Suggested fix direction

Add a host-secret cache-entry signature or MAC over the manifest identity plus assembly hash/bytes at cache write time, store it beside the cache entry, and verify it before accepting a cache hit. Include the plan seal or an equivalent host-owned secret-derived value so a forged manifest cannot be accepted just by recomputing public hashes. If cache sharing across hosts is required, make the trust root explicit through a configured signing key and document that unsigned legacy entries are quarantined or recompiled.

## Deduplication key

security/compiled-cache/artifact-origin/semantic-tamper-evidence-missing
