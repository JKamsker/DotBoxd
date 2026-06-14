---
id: API-0003
area: api_coherence
status: fixed_pending_verification
priority: medium
title: Plugin JSON upload helpers are omitted from package surface guidance
dedup_key: api-plugin-json-upload-package-guidance
created_at: 2026-06-12T22:03:17.1496862+00:00
created_by: Codex completeness auditor
created_commit: 
updated_at: 2026-06-13T06:13:55.4213922+00:00
claimed_by: worker
claimed_at: 2026-06-13T06:12:05.3697182+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T06:13:55.4213922+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0003: Plugin JSON upload helpers are omitted from package surface guidance

## Claim

The package surface guidance does not tell plugin hosts that the production JSON package upload APIs live in `DotBoxd.Kernels.Serialization.Json`, not `DotBoxd.Plugins`. A consumer following the README package list can reasonably install/reference `DotBoxd.Plugins` for plugin manifest, hook, kernel, and message-binding APIs, then fail to find `PluginPackageJsonSerializer` or `InstallJsonAsync` from the addendum upload snippets.

## Why this matters

The addendum positions JSON package upload as the production boundary. If the package list and setup docs do not name the required addon package/namespace, the safest installation path is less discoverable than the local generated factory path.

## Evidence

- `README.md:12` describes `DotBoxd.Kernels.Serialization.Json` only as "JSON IR importer and host import extensions".
- `README.md:19` describes `DotBoxd.Plugins` as the package for live plugin manifest, hook, kernel, and message-binding APIs.
- `src/DotBoxd.Kernels.Serialization.Json/DotBoxd.Kernels.Serialization.Json.csproj` references `DotBoxd.Plugins`, while `src/DotBoxd.Plugins/DotBoxd.Plugins.csproj` does not reference `DotBoxd.Kernels.Serialization.Json`; the JSON upload helpers are therefore in the JSON addon package boundary.
- `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:7` defines `PluginPackageJsonSerializer`, and `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:315` defines `PluginServerJsonExtensions.InstallJsonAsync`.
- `docs/Specs/Addendum/Examples.md:245` and `docs/Specs/Addendum/Examples.md:249` show `PluginPackageJsonSerializer.Export` and `server.InstallJsonAsync`, but the surrounding setup does not call out the required `DotBoxd.Kernels.Serialization.Json` package/reference/namespace.

## Suggested test or benchmark

Add a consumer-facing compile/API test or docs smoke fixture for the production upload snippet that references the intended package set and imports the intended public namespace. The test should fail if only `DotBoxd.Plugins` is referenced for `PluginPackageJsonSerializer`/`InstallJsonAsync`, and the docs should explicitly include `DotBoxd.Kernels.Serialization.Json` for the upload path.

## Suggested fix direction

Update `README.md` current package descriptions and the addendum upload/setup section to state that plugin JSON export/import and `InstallJsonAsync` are provided by `DotBoxd.Kernels.Serialization.Json`. Include the expected `using DotBoxd.Kernels.Serialization.Json;` and package/project reference in the production upload snippet.

## Scope boundaries

Do not move APIs between packages as part of this finding unless the intended public package boundary changes. This finding is about making the existing package surface coherent and discoverable.

## Deduplication key

`api-plugin-json-upload-package-guidance`

## Verification checklist

- [ ] README package list names the plugin JSON upload helpers under the correct package.
- [ ] Addendum upload snippets include the required package/reference/namespace.
- [ ] A consumer-facing compile/docs smoke check proves the documented snippet resolves `PluginPackageJsonSerializer` and `InstallJsonAsync`.
- [ ] No unrelated package dependencies are added.
