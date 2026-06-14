---
id: CMP-0014
area: completeness
status: open
priority: medium
title: Transport-agnostic IPC addon lacks a public generic-transport example
dedup_key: completeness/ipc-dotboxd/generic-transport-example-missing
created_at: 2026-06-12T22:54:10.0687513+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T22:54:10.0687513+00:00
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

# CMP-0014: Transport-agnostic IPC addon lacks a public generic-transport example

## Claim

`DotBoxd.Pushdown.Services` is advertised as a transport-agnostic DotBoxd MessagePack addon, but the maintained public docs and examples only show the named-pipe convenience path. The generic `IServerTransport`/`ITransport` entry points are implemented and unit-covered, yet there is no user-facing example or docs-smoke path proving how a host should wire a non-named-pipe transport through the public addon API.

## Why this matters

The package boundary promises more than named pipes: hosts should be able to reuse the addon for TCP, in-memory test transports, or future DotBoxd transports without pulling transport-specific code into DotBoxd.Kernels core. Without a public generic-transport walkthrough, consumers are likely to copy the named-pipe sample as the only supported shape or bypass the DotBoxd.Kernels wrapper when they need another DotBoxd transport.

## Evidence

- `README.md:14` describes `DotBoxd.Pushdown.Services` as a preview MessagePack IPC addon built on DotBoxd generic transports, with named-pipe convenience helpers.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/03-architecture.md:126` through `docs/Specs/Initial/dotboxd-sandbox-spec/spec/03-architecture.md:132` lists the addon responsibilities as DotBoxd MessagePack transport-agnostic helpers, named-pipe convenience wrappers, and plugin-control IPC transport primitives.
- `src/DotBoxd.Pushdown.Services/DotBoxdDotBoxdRpcMessagePackIpc.cs:21` exposes `Listen(IServerTransport, ...)`, and `src/DotBoxd.Pushdown.Services/DotBoxdDotBoxdRpcMessagePackIpc.cs:31` plus `:40` expose `ConnectAsync(ITransport, ...)` for the generic transport path.
- The public walkthrough remains named-pipe-only: `README.md:132` through `README.md:143` runs the named-pipe server/client, and `docs/Specs/Addendum/Examples.md:328` through `docs/Specs/Addendum/Examples.md:344` documents only the named-pipe sample.
- `examples/PluginIpc/DotBoxd.Kernels.PluginIpc.Server/Program.cs:14` calls `ListenNamedPipe(...)`, and `examples/PluginIpc/DotBoxd.Kernels.PluginIpc.Client/Program.cs:11` calls `ConnectNamedPipeAsync(...)`; no example project demonstrates `Listen(...)` or `ConnectAsync(...)` with a non-named-pipe DotBoxd transport.
- `scripts/check-docs-smoke.ps1:135` skips the IPC smoke on non-Windows runners because the maintained sample is named-pipe-specific.
- `docs/issues/dotboxd-transport-agnostic-ipc-addon.md:101` through `docs/issues/dotboxd-transport-agnostic-ipc-addon.md:106` records the intended acceptance shape, including generic transport entry points and a test or sample demonstrating a non-named-pipe DotBoxd transport. The current public sample half of that acceptance is still missing.
- Existing findings cover related but different gaps: `API-0011` covers prerelease release-channel policy, `API-0006` covers clean NuGet consumption, `COR-0015` covers named-pipe authentication, and `PAL-0014` covers IPC allocation defaults. None require a public generic-transport example or docs-smoke path.

## Suggested acceptance test

Add a small public example or docs-smoke fixture that wires a deterministic non-named-pipe DotBoxd transport through `DotBoxdDotBoxdRpcMessagePackIpc.Listen(...)` and `ConnectAsync(...)`, performs one plugin-control call or minimal RPC call, and runs on Windows, Linux, and macOS. Keep the existing named-pipe sample for deployment guidance, but make the generic transport path visible and continuously checked.

## Suggested fix direction

Create an `examples/PluginIpcGenericTransport` sample or extend the existing IPC example with a mode that uses an in-memory/test DotBoxd transport through the generic DotBoxd.Kernels wrapper. Link it from README and the addendum IPC section, and update `check-docs-smoke.ps1` so at least the generic transport sample runs on every CI OS while the named-pipe sample remains Windows-specific if needed.

## Scope boundaries

Do not change IPC authentication, prerelease dependency policy, or package layout as part of this finding. This is only about proving and documenting the already-implemented transport-agnostic public surface.

## Deduplication key

`completeness/ipc-dotboxd/generic-transport-example-missing`

## Verification checklist

- [ ] Public docs show `Listen(IServerTransport, ...)` and `ConnectAsync(ITransport, ...)` usage outside the named-pipe wrappers.
- [ ] A runnable example demonstrates a non-named-pipe DotBoxd transport through `DotBoxdDotBoxdRpcMessagePackIpc`.
- [ ] Docs smoke runs that generic transport example on all supported CI operating systems.
- [ ] Existing named-pipe docs remain available and clearly scoped to trusted local control-plane deployments.
