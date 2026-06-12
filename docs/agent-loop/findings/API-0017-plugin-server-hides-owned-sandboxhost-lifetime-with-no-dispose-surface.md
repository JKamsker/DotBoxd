---
id: API-0017
area: api_coherence
status: open
priority: medium
title: Plugin server hides owned SandboxHost lifetime with no dispose surface
dedup_key: api/plugins/plugin-server/owned-host-dispose-missing
created_at: 2026-06-12T22:50:34.2636569+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T22:50:34.2636569+00:00
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

# API-0017: Plugin server hides owned SandboxHost lifetime with no dispose surface

## Claim

`PluginServer.Create(...)` constructs and owns an internal `SandboxHost`, but `PluginServer` does not expose `IDisposable`, `Dispose()`, or any documented ownership handoff. Public plugin hosts therefore cannot release the owned host resources when retiring a plugin server.

## Affected public API

- `src/SafeIR.Plugins/PluginServer.cs:6` declares `public sealed class PluginServer` with no disposal interface.
- `src/SafeIR.Plugins/PluginServer.cs:8` stores the owned `SandboxHost` in a private `_host` field.
- `src/SafeIR.Plugins/PluginServer.cs:30` exposes `PluginServer.Create(...)` as the construction API.
- `src/SafeIR.Plugins/PluginServer.cs:42` constructs the host through `SandboxHost.Create(...)`.
- `src/SafeIR.Plugins/PluginServer.cs:57` returns a new `PluginServer` that owns that host.
- `src/SafeIR.Hosting/Execution/SandboxHost.cs:9` declares `SandboxHost : IDisposable`.
- `src/SafeIR.Hosting/Execution/SandboxHost.cs:275` disposes the compiled executable cache from `SandboxHost.Dispose()`.
- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:43` through `:69` documents `SandboxHost` as disposable and says long-lived hosts should dispose it when retired.
- `README.md:29` shows direct `SandboxHost.Create(...)` for minimal host usage, but the plugin examples use `PluginServer.Create(...)` and do not show any equivalent lifetime boundary.

## Inconsistency

The lower-level host API makes ownership explicit with `IDisposable`, while the higher-level plugin API hides the owned host and gives callers no equivalent release path. That is inconsistent with the public API's own lifetime guidance for materialized compiled delegates and host-owned execution resources.

## User impact

A game/server host that creates and retires plugin servers for tenants, worlds, tests, or reloads cannot deterministically release compiled materializations, generated assembly load contexts, hotness state, and other host-owned execution state through the plugin API. The only workaround is to avoid `PluginServer.Create(...)` and reimplement plugin server wiring around a separately owned `SandboxHost`, which defeats the purpose of the public convenience surface.

## Breaking-change risk

Adding `IDisposable` to `PluginServer` and forwarding to the owned host is source-compatible for most callers. The main compatibility decision is whether disposal should also revoke installed kernels and make later `InstallAsync`, `Uninstall`, `Hooks.PublishAsync`, and `Kernels.Get` fail with `ObjectDisposedException`; that should be documented and covered by tests.

## Suggested fix direction

Make `PluginServer` implement `IDisposable` and dispose the private `_host`. Track a disposed flag so public lifecycle and publish APIs fail clearly after disposal. If future worker or IPC integrations need asynchronous cleanup, either also implement `IAsyncDisposable` or document why synchronous disposal is sufficient for the current owned resources.

## Acceptance tests

- Add a public API/lifetime test that creates a plugin server with a compiled host configuration, installs/runs a package, disposes the server, and asserts subsequent install/publish calls fail with `ObjectDisposedException`.
- Add a regression test proving disposing `PluginServer` calls through to the owned `SandboxHost` resource path, for example by injecting a test compiler/materialized compiled artifact that observes host disposal indirectly through the compiled executable cache.
- Update the addendum examples or public API docs to show `using var server = PluginServer.Create(...)` or equivalent lifetime guidance.

## Smallest fixable slice

Implement `IDisposable` on `PluginServer`, call `_host.Dispose()`, guard public entrypoints after disposal, and update one plugin example/doc snippet to show ownership. Do not change plugin installation semantics beyond disposed-object behavior.

## Deduplication key

`api/plugins/plugin-server/owned-host-dispose-missing`
