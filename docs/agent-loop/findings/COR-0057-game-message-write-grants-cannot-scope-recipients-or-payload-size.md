---
id: COR-0057
area: correctness
status: verified
priority: high
title: Game message write grants cannot scope recipients or payload size
dedup_key: security/plugins/game-message-write/unscoped-recipient-payload
created_at: 2026-06-13T06:24:26.0787229+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-13T07:22:20.2079993+00:00
claimed_by: implementer
claimed_at: 2026-06-13T07:12:48.6057894+00:00
claim_branch: 
fixed_by: implementer
fixed_at: 2026-06-13T07:17:23.9787048+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-13T07:22:20.2079993+00:00
verified_commit: 4a92761
duplicate_of: 
---

# COR-0057: Game message write grants cannot scope recipients or payload size

## Evidence

- `src/DotBoxd.Kernels/Policy.cs` implements `GrantGameMessageWrite()` by adding `CapabilityGrant("game.message.write", new Dictionary<string, string>())`; no allowed target, target prefix, channel, or message byte limit can be configured.
- `src/DotBoxd.Plugins/Runtime/PluginMessageBindings.cs` reads `targetId` and `message` from sandbox arguments, only validates the target as an opaque ID, sanitizes control characters, and calls `sink.SendAsync(targetId, message, ...)`.
- The same binding's `ValidateGrant` rejects every grant parameter, so a host that tries to pass `allowedTargets`, `targetPrefix`, `maxMessageLength`, or similar policy state gets a policy diagnostic rather than enforcement.
- `tests/DotBoxd.Kernels.Tests/Misc05/PluginMessageBindingTests.cs` covers missing capability, invalid target syntax, and audit redaction; it does not cover recipient allowlists or payload-size policy because the production grant surface cannot express them.
- This is distinct from `COR-0016`, which removed the unsafe default grant. The remaining issue affects intentionally granted packages: the grant is still all-or-nothing.

## Impact

Any plugin package granted `game.message.write` can emit sanitized messages to any opaque target ID it can construct or receive, not just the event subject or a host-approved recipient set. A compromised or overbroad plugin can send cross-player/system messages or very large payloads to the host sink while policy review can only see the coarse capability request, not enforce recipient or payload limits.

## Suggested fix

Add typed grant parameters for message dispatch, such as allowed target IDs or prefixes/channels and a max message length or byte limit. Validate them during policy preparation and enforce them in `PluginMessageBindings.CreateSend` before calling `IPluginMessageSink`. Keep the current no-parameter helper only as a clearly named unrestricted helper, or require callers to opt into unrestricted dispatch explicitly.
