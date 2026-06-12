# 13 — Cache and Versioning

## Purpose

Compiled mode can persist generated DLLs for faster future execution. Caching must never bypass policy or verifier decisions.

## Cache key

Cache key must include every input that can affect safety or semantics.

Required parts:

```text
canonical IR hash
canonicalizer version
sandbox language version
type system version
compiler version
verifier version
runtime facade version
binding manifest hash
policy hash
target framework/runtime assumptions
optimization flags
determinism mode
```

Example:

```text
sha256(
  "safe-ir-cache-v1" +
  moduleHash +
  canonicalizerVersion +
  languageVersion +
  compilerVersion +
  verifierVersion +
  runtimeFacadeHash +
  bindingManifestHash +
  policyHash +
  targetFramework +
  optimizationFlags
)
```

## Why policy hash matters

Bad scenario without policy hash:

```text
v1 policy grants file.read
module compiles and caches DLL
v2 policy revokes file.read
old DLL is reused and still calls file.read
```

Therefore policy hash must be part of the cache key or execution gate.
For already-prepared plans, the host revocation gate runs before compiled artifact lookup or
materialization, so a revoked capability cannot reuse a stale cache entry.

## Artifact layout

```text
cache/
  ab/cd/<cacheKey>/
    module.dll
    module.pdb optional
    manifest.json
    verification.json
```

Use path-safe hash segments only. Never include user-controlled names in cache paths except sanitized display metadata.

## Manifest

```json
{
  "artifactVersion": 1,
  "cacheKey": "...",
  "moduleHash": "...",
  "planHash": "...",
  "policyHash": "...",
  "bindingManifestHash": "...",
  "runtimeFacadeHash": "...",
  "compilerVersion": "1.2.0",
  "verifierVersion": "1.2.0",
  "languageVersion": "1.0.0",
  "targetFramework": "net10.0",
  "optimizationFlags": ["boxed-values"],
  "assemblyHash": "...",
  "pdbHash": "...",
  "createdAt": "2026-06-11T00:00:00Z"
}
```

## Verification cache

Verification result can be cached by:

```text
assemblyHash + verifierVersion + allowlistHash
```

Still check manifest and current policy before using the artifact.

## Cache write protocol

Use atomic writes:

1. write to temp directory
2. fsync where needed
3. verify DLL from temp directory
4. write manifest/verification result
5. atomic rename to final cache directory

If process crashes, temp directories are safe to clean.

## Cache read protocol

1. compute expected cache key
2. open manifest
3. validate manifest fields
4. hash DLL/PDB
5. verify or load cached verification result
6. load only after verification passes

On mismatch:

- delete or quarantine artifact
- recompile or interpret
- audit cache failure

## Cache invalidation

Invalidate when any of these changes:

- canonicalizer version or canonical IR encoding rules
- compiler version
- verifier version
- runtime facade assembly
- binding manifest
- policy
- type checker/effect system semantics
- target framework/runtime strategy
- optimization flags

## Binding versioning

Changing a binding requires a binding version/hash update if it changes:

- semantics
- effects
- resource costs
- audit behavior
- signature
- capability requirements
- compiled stub target

## Runtime facade versioning

The verifier allowlist depends on runtime facade methods. Any change to allowed method signatures should change runtime facade hash.

The runtime facade hash also includes the loaded SafeIR core/runtime assembly identities,
including module version IDs. This invalidates compiled artifacts when runtime facade
implementations change without a public signature change.

## Cache security

Cache storage must be protected:

- host process can write
- untrusted users cannot write
- worker process preferably read-only after artifact creation
- no shared writable cache across tenants unless carefully isolated

The implementation validates the cache root before use: it must be a real directory rather than
a reparse point, must allow exclusive host writes, and on Unix must not be group- or
world-writable. Windows deployments should place the cache under a host-controlled profile or
service directory with equivalent ACLs.

## Multi-tenant cache

Options:

### Per-tenant cache

Pros:

- simple isolation
- easy deletion
- policy-specific naturally

Cons:

- duplicate artifacts

### Shared cache

Pros:

- deduplication

Cons:

- more security care
- must include policy hash/capability constraints
- risk of metadata leakage through cache timing or artifact names

Recommendation:

Start with per-tenant or per-trust-zone cache. Add shared cache only after correctness is proven.

## Cache and interpreted mode

Interpreted mode also benefits from caching:

- parsed module
- canonical IR
- type-checked plan
- effect analysis
- execution plan

This is separate from DLL cache.

Example:

```text
json hash -> imported module
canonical IR hash -> validated module
plan hash -> validated execution plan
```

## Cache eviction

The current implementation validates, publishes, reads, and quarantines cache entries; it does not
run an automatic eviction loop. Hosts that need bounded disk usage should evict by:

- least recently used
- max disk size
- max artifact age
- tenant deletion
- policy/binding/runtime version changes

Never evict audit records solely because code cache was evicted. Eviction code must use the same
root/path guards as normal cache access and must not delete outside the configured cache root.

## Audit

Every execution should log:

```text
executionMode: interpreted | compiled
cacheStatus: None | Hit | Miss | Invalid | Recompiled
cacheKey optional
assemblyHash optional
planHash
policyHash
bindingManifestHash
```
