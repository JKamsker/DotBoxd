---
id: CMP-0006
area: completeness
status: open
priority: high
title: Plugin setup docs rely on implicit event adapter discovery instead of explicit server whitelist
dedup_key: cmp-plugin-explicit-event-adapter-whitelist-setup-docs
created_at: 2026-06-12T22:03:18.4741568+00:00
created_by: Codex completeness auditor
created_commit: 
updated_at: 2026-06-12T22:03:18.4741568+00:00
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

# CMP-0006: Plugin setup docs rely on implicit event adapter discovery instead of explicit server whitelist

## Claim

The plugin setup docs do not show the server owner creating an explicit event adapter whitelist before installing or wiring plugin hooks. The docs say the shared contract assembly is the plugin developer's visible universe, but every runnable setup path relies on implicit adapter discovery or convention fallback instead of making the approved server event shapes explicit.

## Why this matters

The adapter is the host-owned boundary that decides which event fields become sandbox input. If setup guidance omits explicit registration, server owners may treat convention reflection as the recommended production posture and accidentally expose event properties that were not intentionally reviewed.

## Evidence

- `docs/Specs/Addendum/Addendum.md:36` introduces the server-provided shared contract assembly, and `docs/Specs/Addendum/Addendum.md:127` says the contract assembly is the plugin developer's visible universe.
- `docs/Specs/Addendum/Examples.md:115` creates `PluginServer.Create(messages)` and proceeds to install/register hooks without showing `RegisterEventAdapter`.
- `src/SafeIR.Plugins/PluginServer.cs:66` exposes `RegisterEventAdapter<TEvent>(IPluginEventAdapter<TEvent> adapter)` as the explicit host API.
- `src/SafeIR.Plugins/Runtime/PluginEventAdapterRegistry.cs:15` resolves unregistered adapters, and `src/SafeIR.Plugins/Runtime/PluginEventAdapterRegistry.cs:20` falls back to discovery or `ConventionEventAdapter<TEvent>.Create()`.
- `examples/PluginIpc/SafeIR.PluginIpc.Server.Abstractions/DamageEvent.cs:14` provides an explicit `DamageEventAdapter`, but `examples/LocalPlugin/SafeIR.PluginLocal/Program.cs:6` and `examples/PluginIpc/SafeIR.PluginIpc.Server/PluginControlService.cs:28` create a server without registering it.

## Suggested test or benchmark

Add a docs/example smoke assertion that the production setup walkthrough and flagship examples register `DamageEventAdapter.Instance` (or pass the adapter explicitly to `Hooks.On(adapter)`) before installing or publishing. The smoke should fail if the documented production path uses only `Hooks.On<TEvent>()` with no explicit adapter registration/setup note.

## Suggested fix direction

Update the addendum setup docs and flagship examples to show a server-side whitelist step, for example `server.RegisterEventAdapter(DamageEventAdapter.Instance);`, and explain that convention/discovery is a development convenience while production servers should register reviewed adapters/contracts explicitly.

## Scope boundaries

Do not change runtime adapter discovery behavior in this finding. Keep the fix to setup docs, examples, and smoke coverage that proves the documented production posture.

## Deduplication key

`cmp-plugin-explicit-event-adapter-whitelist-setup-docs`

## Verification checklist

- [ ] Production setup docs include an explicit event adapter whitelist/registration step.
- [ ] Flagship local and IPC examples demonstrate the explicit adapter path.
- [ ] Docs smoke or example coverage fails if the explicit setup step disappears.
- [ ] Runtime behavior is not broadened.
