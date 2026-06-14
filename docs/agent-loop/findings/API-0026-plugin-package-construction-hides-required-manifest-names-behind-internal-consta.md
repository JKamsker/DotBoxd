---
id: API-0026
area: api_coherence
status: open
priority: medium
title: Plugin package construction hides required manifest names behind internal constants
dedup_key: api/plugins/package-construction/public-manifest-contract-names
created_at: 2026-06-13T06:52:07.8107358+00:00
created_by: core-completeness-producer
created_commit: 
updated_at: 2026-06-13T06:52:07.8107358+00:00
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

# API-0026: Plugin package construction hides required manifest names behind internal constants

## Claim

`DotBoxd.Plugins` exposes hand-authored plugin package construction through `PluginManifest`, `PluginPackage.Create(...)`, `KernelEntrypoints`, `LiveSettingDefinition`, and raw `SandboxModule.Metadata`, but the names required to produce a package that passes runtime validation are kept in internal-only constants. Consumers who do not use the source generator must duplicate string literals such as module metadata keys, live-setting type names, default entrypoint names, event parameter prefixes, and the `IEventKernel<TEvent>` contract shape.

This is a package-facing API completeness gap: the public model is constructible, but the supported vocabulary and helper surface that make the model valid are not public.

## Evidence

- `src/DotBoxd.Plugins/PluginManifest.cs` declares public `PluginManifest`, `LiveSettingDefinition`, `HookSubscriptionManifest`, `KernelEntrypoints`, and `PluginPackage.Create(...)`, so package consumers can construct plugin packages directly.
- `src/DotBoxd.Plugins/PluginManifestNames.cs` keeps the required package vocabulary internal: live setting types `"bool"`, `"int"`, `"long"`, `"double"`, `"string"`; module metadata keys `"pluginId"` and `"kernel"`; default entrypoints `"ShouldHandle"` and `"Handle"`; the `IEventKernel<...>` contract framing; and the `"e_"` event parameter prefix.
- `src/DotBoxd.Plugins/Runtime/PluginPackageValidator.cs` validates public packages against those internal names: module metadata must contain the internal `pluginId` and `kernel` keys, entrypoints must exist under the configured/default names, effects must parse, and live setting type strings must be supported.
- `src/DotBoxd.Plugins/Runtime/PluginPreparedPackageValidator.cs` validates the manifest contract by parsing the internal `IEventKernel<...>` prefix/suffix and validates live-setting parameters using the internal live-setting type converter.
- `src/DotBoxd.Plugins.Analyzer/Analysis/DotBoxdPackageSourceEmitter.cs` can emit valid packages because it has its own internal `DotBoxdGenerationNames` constants for metadata keys, entrypoint names, manifest type strings, event prefix, and contract names. A normal package consumer cannot reference those generator/runtime constants.
- `src/DotBoxd.Plugins/Runtime/PluginMessageBindings.cs` already exposes public constants for `game.message.send` and `game.message.write`, showing capability/binding IDs are treated as package-facing constants when they are part of the supported contract.

## Impact

Hand-authored package producers, package transformation tools, tests, and non-generator integrations must reverse-engineer and copy internal strings to create a package that `PluginServer.InstallAsync(...)` or `PluginPackageJsonSerializer.Import(...)` accepts. A typo or future internal rename becomes a runtime validation failure rather than a compile-time/API-guided construction error. This also makes the public `PluginPackage.Create(...)` surface look complete while the actual valid package contract is only reachable through source generator internals.

## Suggested fix direction

Expose the supported package vocabulary through public API, or add a public builder that hides it. Viable small slices include:

- A public constants surface such as `PluginManifestNames`, `LiveSettingTypes`, `PluginModuleMetadataKeys`, and `PluginKernelEntrypoints`.
- Public factory helpers such as `KernelEntrypoints.Default`, `PluginManifest.ContractFor<TEvent>()`, and `LiveSettingDefinition.Create<T>(...)`.
- A `PluginPackageBuilder` that creates required module metadata and default entrypoints without consumers spelling internal literals.

Add a consumer-facing test that creates a minimal hand-authored package using only public constants/builders and no string literals for required DotBoxd.Kernels-owned metadata, then validates/imports/installs it.

## Non-duplicates checked

Existing plugin findings cover analyzer diagnostic documentation, runtime diagnostic documentation, JSON import validation, manifest inspection examples, installed-kernel inventory, and message-write policy. None track the source-level gap where the public package construction model depends on internal-only manifest vocabulary.

## Deduplication key

`api/plugins/package-construction/public-manifest-contract-names`
