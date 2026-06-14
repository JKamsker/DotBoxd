---
id: COR-0015
area: correctness
status: verified
priority: high
title: Named-pipe plugin IPC control plane has no authentication boundary
dedup_key: security/plugin-ipc/named-pipe-control-plane/no-auth
created_at: 2026-06-12T22:06:42.2092451+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T22:55:04.4652948+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:45:44.6310409+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T22:51:06.7342074+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T22:55:04.4652948+00:00
verified_commit: 
duplicate_of: 
---

# COR-0015: Named-pipe plugin IPC control plane has no authentication boundary

# Named-pipe plugin IPC control plane has no authentication boundary

## Summary

The DotBoxd named-pipe plugin IPC helper and runnable sample expose a plugin control-plane service to any local client that knows the pipe name. The boundary is documented as trusted, but the helper and example do not enforce authentication, peer authorization, a one-time handshake, or safe pipe-name entropy.

## Evidence

- `src/DotBoxd.Pushdown.Services/DotBoxdDotBoxdRpcMessagePackIpc.cs` exposes `ListenNamedPipe(string pipeName, ...)` as `Listen(new NamedPipeServerTransport(pipeName), configurePeer, options)` and `ConnectNamedPipeAsync(...)` as `ConnectAsync(new NamedPipeClientTransport(...), ...)`. The DotBoxd.Kernels wrapper adds client `RejectInboundCalls = true`, but it does not add server-side authentication, caller authorization, pipe-name validation, or a shared-secret/session handshake.
- `examples/PluginIpc/DotBoxd.Kernels.PluginIpc.Server/Program.cs` accepts a single command-line pipe name and immediately publishes `PluginControlService` to every peer via `peer.ProvidePluginControlService(service)`.
- `examples/PluginIpc/DotBoxd.Kernels.PluginIpc.Shared/PluginControlContracts.cs` exposes state-changing methods over that pipe: `SetSettingAsync`, `ModifySettingsAsync`, and `PublishDamageAsync`.
- `docs/Specs/Addendum/Examples.md` explicitly says the pipe name is a trusted local control-plane endpoint and asks operators to pass a high-entropy or deployment-scoped name, but the code path does not enforce that posture.

## Impact

A same-user or same-machine process that discovers or guesses the pipe name can connect as a plugin IPC client and mutate live settings or publish events. For the sample and for hosts copying the helper, the plugin IPC boundary becomes name secrecy only, despite exposing privileged control-plane operations.

## Test idea

Add a named-pipe integration test that starts the sample-style service with a predictable pipe name and attempts to call `ModifySettingsAsync` from a second unauthenticated client. The desired fixed behavior should reject the call unless the client completes an explicit DotBoxd.Kernels authentication/authorization handshake, or `ListenNamedPipe` should reject low-entropy/non-scoped names unless an unsafe development option is explicitly selected.

## Suggested fix

Add DotBoxd.Kernels-owned IPC options that require either a shared secret, one-time bearer token, nonce challenge, or host-provided peer authorization callback before services are exposed. At minimum, make the named-pipe convenience helper fail closed for low-entropy names unless a clearly named development-only option is passed, and update the sample to generate and pass an authenticated endpoint token.
