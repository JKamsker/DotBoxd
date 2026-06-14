---
id: COR-0063
area: correctness
status: verified
priority: high
title: Plugin message target IDs bypass audit redaction
dedup_key: security/plugins/message-audit/target-id-redaction
created_at: 2026-06-13T06:31:54.2914811+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-13T07:22:21.5657677+00:00
claimed_by: implementer
claimed_at: 2026-06-13T07:12:49.8073757+00:00
claim_branch: 
fixed_by: implementer
fixed_at: 2026-06-13T07:17:25.1202281+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-13T07:22:21.5657677+00:00
verified_commit: 4a92761
duplicate_of: 
---

# COR-0063: Plugin message target IDs bypass audit redaction

## Evidence

`src/DotBoxd.Plugins/Runtime/PluginMessageBindings.cs` reads the sandbox-controlled `targetId` argument and only checks `SandboxLiteralConstraints.IsOpaqueId(targetId)` before using it. That opaque ID validator allows letters, digits, `_`, `-`, `.`, and `:`, so secret-shaped values such as `token:abc123` are valid target IDs.

The same binding sanitizes the message body before audit with `AuditTextSanitizer.SanitizeAndRedact(message)`, but writes the resource as `ResourceId: $"player:{targetId}"` without applying the sanitizer or a resource-ID-specific redaction helper. `tests/DotBoxd.Kernels.Tests/Misc05/PluginMessageBindingTests.cs` covers message-body redaction but does not assert that `PluginMessage.ResourceId` redacts secret-shaped target IDs.

This is distinct from `COR-0057`: that finding is about grant policy scope for recipients and payload size. This issue is specifically that audit materialization preserves a sandbox-controlled recipient identifier verbatim even when it matches existing secret redaction patterns.

## Impact

Audit streams are commonly exported outside the immediate sandbox trust boundary. A plugin can send a message to a syntactically valid but secret-shaped target ID and have that value persisted in `SandboxAuditEvent.ResourceId`, bypassing the redaction behavior applied to the message body and other path-like audit resources.

## Suggested fix

Add a plugin-message resource sanitizer before writing `ResourceId`, for example by running the target portion through `AuditTextSanitizer.SanitizeAndRedact` or a dedicated resource-ID sanitizer that preserves non-secret player IDs while redacting secret-shaped markers and values. Add a regression test that sends to a valid opaque ID such as `token:abc123`, asserts the sink payload/target behavior remains unchanged, and asserts the `PluginMessage` audit `ResourceId` contains neither `abc123` nor an unredacted secret token marker.
