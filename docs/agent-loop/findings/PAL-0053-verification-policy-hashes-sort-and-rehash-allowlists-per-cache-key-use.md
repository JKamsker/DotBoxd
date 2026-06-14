---
id: PAL-0053
area: perf_alloc
status: open
priority: medium
title: Verification policy hashes sort and rehash allowlists per cache-key use
dedup_key: alloc/verifier-policy/hash/recompute-per-cache-key
created_at: 2026-06-13T07:03:39.5053130+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T07:03:39.5053130+00:00
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

# PAL-0053: Verification policy hashes sort and rehash allowlists per cache-key use

## Claim

`VerificationPolicy.AllowlistHash` and `VerificationPolicy.RuntimeFacadeHash` are stable for a policy instance, but each property access sorts, joins, encodes, hashes, and lowercases the frozen allowlist data again. Cache-key and manifest identity construction read these hashes on compiler/cache paths, so repeated cache operations pay allocation and CPU proportional to verifier allowlist size instead of reusing cached policy metadata.

## Evidence

- `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs:20` through `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs:32` freeze all policy allowlist/runtime-facade sets on construction and init, making the hash inputs stable for the policy instance.
- `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs:145` through `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs:152` recomputes `AllowlistHash` by concatenating the frozen sets on every property access.
- `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs:154` through `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs:159` recomputes `RuntimeFacadeHash` by filtering allowed members and concatenating runtime facade identities on every property access.
- `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs:161` through `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs:164` sorts the values, joins them into one string, UTF-8 encodes that string, computes SHA-256, and lowercases the hex result for each hash access.
- `src/DotBoxd.Kernels.Compiler/CacheKeyBuilder.cs:20` through `src/DotBoxd.Kernels.Compiler/CacheKeyBuilder.cs:41` reads `policy.AllowlistHash` and `policy.RuntimeFacadeHash` for every cache-key build.
- `src/DotBoxd.Kernels.Compiler/CacheKeyBuilder.cs:49` through `src/DotBoxd.Kernels.Compiler/CacheKeyBuilder.cs:63` calls `Build(...)` and then reads `policy.RuntimeFacadeHash` again while constructing the manifest identity.
- Existing `COR-0029` covered mutable policy allowlists as a correctness/cache-integrity issue, `PAL-0020` covers `SandboxPolicy.Hash`, and `ALG-0017` covers cache-key rebuilding before materialized compiled-cache hits. This finding is the remaining allocation in stable verifier-policy hash access itself.

## Impact

Compiled cache lookup, persistent cache validation, compilation, manifest construction, and tests/tools that build many cache keys can read these stable hashes repeatedly. With the default policy this still allocates ordered/enumeration state, joined strings, UTF-8 byte arrays, and lowercase hash strings. With custom verifier policies or larger runtime facades, the cost grows with allowed assemblies, types, members, prefixes, and runtime facade identities, even though the policy metadata does not change.

## Better target

Compute `AllowlistHash` and `RuntimeFacadeHash` once when `VerificationPolicy` freezes its inputs, or lazily cache them in thread-safe readonly/lazy fields tied to the frozen sets. `with`/init updates should freeze the replacement set and refresh the cached hashes once. Cache-key and manifest identity builders should read cached string fields, not rebuild sorted hash material on every access.

## Benchmark/allocation test idea

Add allocation benchmarks for `CacheKeyBuilder.Build` and `CacheKeyBuilder.BuildManifestIdentity` using `VerificationPolicy.BoxedValueDefaults()` and a custom policy with 100, 1,000, and 10,000 allowed members/types. Execute 1, 1,000, and 100,000 cache-key builds and assert verifier-policy hash access does not allocate or sort allowlist data on repeated calls for the same policy instance.
