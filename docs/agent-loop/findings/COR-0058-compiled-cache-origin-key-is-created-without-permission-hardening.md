---
id: COR-0058
area: correctness
status: fixed_pending_verification
priority: high
title: Compiled cache origin key is created without permission hardening
dedup_key: security/compiled-cache/origin-key-permissions
created_at: 2026-06-13T06:24:27.5233123+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-13T07:49:31.1743921+00:00
claimed_by: fixer
claimed_at: 2026-06-13T07:49:31.0323976+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-13T07:49:31.1743921+00:00
fixed_commit: b14fd0a
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0058: Compiled cache origin key is created without permission hardening

## Evidence

- `src/DotBoxd.Kernels.Compiler/Internal/CacheIntegrity/PersistentCompiledArtifactCacheOrigin.cs` signs cached assembly and manifest data with an HMAC key loaded by `ReadOrCreateOriginKeyAsync`.
- `ReadOrCreateOriginKeyAsync` stores the key at the user-local `DotBoxd.Kernels/compiled-cache-origin.key` path, calls `Directory.CreateDirectory`, reads any existing 32-byte file, and uses `DurableCreate` to write new random bytes. It never validates ACLs or Unix modes for either the directory or the key file, and it does not create the key with explicit owner-only permissions.
- By contrast, `src/DotBoxd.Kernels.Compiler/Internal/PersistentCompiledArtifactCacheRootGuard.cs` rejects group/world-writable Unix cache roots and broad Windows write ACLs; the same hardening is absent for the HMAC trust root.
- `tests/DotBoxd.Kernels.Tests/Compiled/Core/CacheIntegrity/CompiledCacheOriginTests.cs` covers stale proof quarantine after bytes are forged, but there is no test for permissive origin-key ACLs/modes or an attacker-provided 32-byte key file.
- This is distinct from `COR-0048`, which required an origin proof. This finding is about the protection of the proof signing key.

## Impact

The persistent cache now relies on the origin key as a host secret. If the key file is readable or replaceable by another local user or process through inherited permissions, that actor can compute valid `origin.json` signatures for verifier-safe but semantically hostile cache entries, or rotate the key to force cache invalidation. The cache root can be private while the actual signing key remains weaker than the cache trust boundary.

## Suggested fix

Create the origin-key directory and key with owner-only permissions. On read, reject or rotate keys whose file or directory ACL/Unix mode grants broad read/write access, and fail closed if permissions cannot be verified. Add tests mirroring `CompiledCacheRootGuardTests` for group/world/broad-principal access on the origin-key path and for preexisting attacker-created key files.
