---
id: COR-0019
area: correctness
status: verified
priority: medium
title: Capability revocation audit reasons bypass secret redaction
dedup_key: security/audit/capability-revocation-reason/no-secret-redaction
created_at: 2026-06-12T22:10:51.8866002+00:00
created_by: continuous-security-producer
created_commit: 
updated_at: 2026-06-12T22:18:09.5342352+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:12:07.5625647+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T22:13:59.3186598+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T22:18:09.5342352+00:00
verified_commit: 
duplicate_of: 
---

# COR-0019: Capability revocation audit reasons bypass secret redaction

## Claim

Capability revocation audit records write the host-provided revocation reason verbatim after only control-character cleanup, bypassing the existing audit secret redaction used by sandbox log and plugin-message payloads.

## Evidence

`src/DotBoxd.Hosting/Execution/SandboxHost.Capabilities.cs` accepts `RevokeCapability(string capabilityId, string reason = "")` and `SanitizeReason` only trims, replaces control characters with spaces, and truncates to 256 characters. It does not call `AuditTextSanitizer.SanitizeAndRedact` or equivalent secret-pattern redaction.

`src/DotBoxd.Hosting/SandboxHost.Results.cs` then writes the sanitized reason into the public audit event as both `Message: revoked.Reason` and `Fields["reason"] = revoked.Reason` in `CapabilityRevokedResult`. Existing `tests/DotBoxd.Kernels.Tests/Misc01/CapabilityRevocationTests.cs` assert the exact reason is preserved in both locations.

By contrast, `src/DotBoxd.Kernels.Runtime/Bindings/SafeLogBindings.cs` and `src/DotBoxd.Plugins/Runtime/PluginMessageBindings.cs` call `AuditTextSanitizer.SanitizeAndRedact` before placing attacker-controlled text into audit `Message`. `src/DotBoxd.Kernels.Runtime/AuditTextSanitizer.cs` covers common `token=`, `password:`, authorization header, bearer/basic scheme, and URI credential patterns, but revocation reasons do not use that path.

## Impact

Revocation reasons often come from operator consoles, webhooks, incident tooling, or tenant-control APIs. If a caller includes `token=...`, `Authorization: Bearer ...`, a signed URL, or a copied secret in the reason, DotBoxd.Kernels persists and forwards it in audit streams even though similar strings are redacted in sandbox log/plugin-message audit. This leaves an audit-export leak outside the sandbox boundary and makes redaction behavior inconsistent across audit event kinds.

## Security test idea

Extend `CapabilityRevocationTests` with a reason such as `token=abc123 Authorization: Bearer secret.jwt`, revoke a required capability, execute the prepared plan, and assert that neither `CapabilityRevoked.Message` nor `CapabilityRevoked.Fields["reason"]` contains the raw secret values.

## Suggested fix direction

Run revocation reasons through the same centralized audit redaction function used for sandbox logs and plugin messages before assigning them to audit `Message` or `Fields`. If hosts need exact internal reasons, keep that data out of the exported `SandboxAuditEvent` or expose it through an explicitly trusted diagnostic-only channel.
