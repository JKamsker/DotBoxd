---
id: API-0028
area: api_coherence
status: open
priority: medium
title: Capability revocation is write-only on SandboxHost
dedup_key: api/hosting/capability-revocation/write-only-control-plane
created_at: 2026-06-13T06:58:43.0587811+00:00
created_by: core-completeness-producer
created_commit: 
updated_at: 2026-06-13T06:58:43.0587811+00:00
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

# API-0028: Capability revocation is write-only on SandboxHost

## Claim

`DotBoxd.Hosting` exposes capability revocation as a public runtime control-plane operation, but the surface is write-only. Hosts can call `SandboxHost.RevokeCapability(...)`, yet there is no public way to query whether a capability is revoked, enumerate revoked capabilities, inspect the sanitized revocation reason/timestamp, or clear/replace a revocation intentionally.

That makes an advertised host control-plane capability incomplete for operational callers.

## Evidence

- `src/DotBoxd.Hosting/Execution/SandboxHost.Capabilities.cs` declares public `RevokeCapability(string capabilityId, string reason = "")`.
- The same file stores revocations in a private `_revokedCapabilities` dictionary and a private `RevokedCapability` record.
- `TryGetRevokedCapability(...)` is private and is only used during `SandboxHost.ExecuteAsync(...)` before dispatch.
- `CapabilityRevokedResult(...)` in `src/DotBoxd.Hosting/SandboxHost.Results.cs` emits the revoked id, reason, and timestamp into audit fields when a run is blocked, but consumers cannot inspect that state directly before attempting execution.
- There is no public `IsCapabilityRevoked`, `TryGetRevokedCapability`, `RevokedCapabilities` snapshot, `ClearCapabilityRevocation`, or public revocation model in the hosting surface.

## Impact

Hosts that expose admin UI, hot-reload policy controls, incident response tooling, or plugin lifecycle controls cannot present the current revocation state from the `SandboxHost` itself. They must maintain a shadow registry around every `RevokeCapability(...)` call and keep it synchronized with host replacement, tests, and future revocation behavior. If revocation is intended to be permanent for a host lifetime, the API also does not make that contract explicit.

## Suggested fix direction

Add a small read/control surface for capability revocations. A source-compatible option is a public immutable `RevokedCapabilityInfo` model plus `TryGetRevokedCapability(string id, out RevokedCapabilityInfo info)` and `IReadOnlyList<RevokedCapabilityInfo> GetRevokedCapabilities()`. If revocations are meant to be reversible, also add an explicit clear/restore method; if they are intentionally one-way, document and encode that in the API shape and tests.

## Non-duplicates checked

Existing revocation findings cover audit redaction/determinism and in-flight plugin revocation behavior. `ALG-0005` mentions revoked-capability checks as a performance target. None cover the source-level API gap where `SandboxHost` exposes only a write-only revocation operation with no public query or inventory surface.

## Deduplication key

`api/hosting/capability-revocation/write-only-control-plane`

## Verification checklist

- [ ] Public hosting API can inspect current capability revocation state without attempting execution.
- [ ] The public revocation model exposes sanitized reason and timestamp if those remain part of the contract.
- [ ] Tests cover revocation query behavior before and after a blocked run.
- [ ] Existing execution-time revocation enforcement remains unchanged.
