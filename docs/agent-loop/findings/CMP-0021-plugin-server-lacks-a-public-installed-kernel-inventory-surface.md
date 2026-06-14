---
id: CMP-0021
area: completeness
status: open
priority: medium
title: Plugin server lacks a public installed-kernel inventory surface
dedup_key: completeness/plugins/admin-inventory/installed-kernel-enumeration
created_at: 2026-06-12T23:34:54.8386911+00:00
created_by: codex-completeness-api-producer
created_commit: 
updated_at: 2026-06-12T23:34:54.8386911+00:00
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

# CMP-0021: Plugin server lacks a public installed-kernel inventory surface

## Claim

`DotBoxd.Plugins` exposes `PluginServer.InstallAsync(...)`, `PluginServer.Uninstall(...)`, and `PluginServer.Kernels`, but the public registry only supports throwing lookups by plugin id. There is no public way to enumerate installed kernels, try-get a kernel, or obtain a stable snapshot of installed plugin manifests for an admin UI.

The addendum documentation positions manifest data as server-owner/admin UI inventory material, but the shipped server surface does not let that UI discover which plugins are currently installed without maintaining a separate shadow index outside the server.

## Why this matters

A real plugin host needs to show currently installed plugins, their live settings, requested effects, subscriptions, execution status, and revocation/uninstall state. Without an inventory surface, every consumer must wrap `InstallAsync` and `Uninstall` to mirror server state, which is easy to desynchronize from replacement installs, failed installs, and future lifecycle behavior. It also makes the public `Kernels` registry incomplete: it exposes direct lookup but not discoverability.

## Evidence

- `README.md:20` presents `DotBoxd.Plugins` as the package for live plugin manifest, hook, kernel, and message-binding APIs.
- `docs/Specs/Addendum/Examples.md:207` says the plugin package manifest exposes permissions and subscriptions that an admin UI or server owner can inspect before enabling the plugin.
- `docs/Specs/Addendum/Examples.md:237` says this is the data a server owner needs to show settings, defaults, ranges, requested effects, and hook subscriptions before install.
- `src/DotBoxd.Plugins/PluginServer.cs:27` exposes `public KernelRegistry Kernels { get; }`, and `src/DotBoxd.Plugins/PluginServer.cs:81` stores installed kernels in that registry.
- `src/DotBoxd.Plugins/PluginServer.cs:86` exposes uninstall by plugin id, but `src/DotBoxd.Plugins/PluginServer.cs:89` through `src/DotBoxd.Plugins/PluginServer.cs:108` only provide `Get(string pluginId)` and `Get<TState>(string pluginId)` as public registry methods. The add, remove, and replacement paths are internal, and there is no `TryGet`, `List`, `Snapshot`, or manifest inventory API.

## Suggested test or benchmark

Add a public API/consumer test that installs two plugin packages, replaces one plugin id with a new package, uninstalls the other, and asserts a public inventory snapshot reports exactly the currently installed plugin ids, manifests, live setting definitions, revocation state, and last execution summary without depending on a consumer-maintained side dictionary.

## Suggested fix direction

Add a small inventory surface to `KernelRegistry` or `PluginServer`, such as `TryGet(string pluginId, out InstalledKernel kernel)` and `IReadOnlyList<InstalledKernel> Snapshot()` or a narrower immutable `InstalledPluginInfo` snapshot. The snapshot should be safe to enumerate while installs and uninstalls occur and should expose enough manifest/live-setting/status data for admin UI rendering without requiring callers to mutate server internals.

## Scope boundaries

Do not change plugin execution semantics, hook dispatch ordering, package validation, IPC control-plane behavior, or plugin message policy while adding the inventory surface. This is only about discoverability of already installed plugin state.

## Deduplication key

`completeness/plugins/admin-inventory/installed-kernel-enumeration`

## Verification checklist

- [ ] Reproduction or test exists where practical.
- [ ] Fix addresses root cause.
- [ ] Relevant tests pass.
- [ ] Perf/allocation evidence exists where practical.
- [ ] No unrelated behavior changed.
