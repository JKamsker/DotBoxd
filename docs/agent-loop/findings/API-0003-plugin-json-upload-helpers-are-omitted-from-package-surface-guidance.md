---
id: API-0003
area: api_coherence
status: open
priority: medium
title: Plugin JSON upload helpers are omitted from package surface guidance
dedup_key: api-plugin-json-upload-package-guidance
created_at: 2026-06-12T22:03:17.1496862+00:00
created_by: Codex completeness auditor
created_commit: 
updated_at: 2026-06-12T22:03:17.1496862+00:00
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

# API-0003: Plugin JSON upload helpers are omitted from package surface guidance

## Claim

The package surface guidance does not tell plugin hosts that the production JSON package upload APIs live in `SafeIR.Serialization.Json`, not `SafeIR.Plugins`. A consumer following the README package list can reasonably install/reference `SafeIR.Plugins` for plugin manifest, hook, kernel, and message-binding APIs, then fail to find `PluginPackageJsonSerializer` or `InstallJsonAsync` from the addendum upload snippets.

## Why this matters

The addendum positions JSON package upload as the production boundary. If the package list and setup docs do not name the required addon package/namespace, the safest installation path is less discoverable than the local generated factory path.

## Evidence

- `README.md:12` describes `SafeIR.Serialization.Json` only as "JSON IR importer and host import extensions".
- `README.md:19` describes `SafeIR.Plugins` as the package for live plugin manifest, hook, kernel, and message-binding APIs.
- `src/SafeIR.Serialization.Json/SafeIR.Serialization.Json.csproj` references `SafeIR.Plugins`, while `src/SafeIR.Plugins/SafeIR.Plugins.csproj` does not reference `SafeIR.Serialization.Json`; the JSON upload helpers are therefore in the JSON addon package boundary.
- `src/SafeIR.Serialization.Json/PluginPackageJsonSerializer.cs:7` defines `PluginPackageJsonSerializer`, and `src/SafeIR.Serialization.Json/PluginPackageJsonSerializer.cs:315` defines `PluginServerJsonExtensions.InstallJsonAsync`.
- `docs/Specs/Addendum/Examples.md:245` and `docs/Specs/Addendum/Examples.md:249` show `PluginPackageJsonSerializer.Export` and `server.InstallJsonAsync`, but the surrounding setup does not call out the required `SafeIR.Serialization.Json` package/reference/namespace.

## Suggested test or benchmark

Add a consumer-facing compile/API test or docs smoke fixture for the production upload snippet that references the intended package set and imports the intended public namespace. The test should fail if only `SafeIR.Plugins` is referenced for `PluginPackageJsonSerializer`/`InstallJsonAsync`, and the docs should explicitly include `SafeIR.Serialization.Json` for the upload path.

## Suggested fix direction

Update `README.md` current package descriptions and the addendum upload/setup section to state that plugin JSON export/import and `InstallJsonAsync` are provided by `SafeIR.Serialization.Json`. Include the expected `using SafeIR.Serialization.Json;` and package/project reference in the production upload snippet.

## Scope boundaries

Do not move APIs between packages as part of this finding unless the intended public package boundary changes. This finding is about making the existing package surface coherent and discoverable.

## Deduplication key

`api-plugin-json-upload-package-guidance`

## Verification checklist

- [ ] README package list names the plugin JSON upload helpers under the correct package.
- [ ] Addendum upload snippets include the required package/reference/namespace.
- [ ] A consumer-facing compile/docs smoke check proves the documented snippet resolves `PluginPackageJsonSerializer` and `InstallJsonAsync`.
- [ ] No unrelated package dependencies are added.
