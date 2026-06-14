---
id: API-0021
area: api_coherence
status: open
priority: medium
title: Module JSON exporter is omitted from public package guidance and smoke coverage
dedup_key: api/json/module-exporter/package-guidance-smoke
created_at: 2026-06-13T06:24:01.5399490+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T06:24:01.5399490+00:00
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

# API-0021: Module JSON exporter is omitted from public package guidance and smoke coverage

## Claim

`DotBoxdJsonExporter` is shipped as a public module serialization API, but the public package guidance and package consumer smoke only prove JSON import and plugin package upload. A user can discover `DotBoxdJsonImporter` and `PluginPackageJsonSerializer` from README/package smoke, while the module export surface is only present in the public API spec and source.

## Why this matters

Programmatic tooling that builds or transforms `SandboxModule` instances needs a supported way to serialize them back to JSON IR. If the exporter namespace, package placement, or transitive dependencies drift, the current release gates can still pass because no package-backed consumer compiles or calls the exporter. Users also have no README-level install/namespace guidance for the round-trip path.

## Evidence

- `src/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonExporter.cs:6` declares public `DotBoxdJsonExporter`.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:432` lists `DotBoxdJsonExporter` in the JSON serialization public API.
- `README.md:14` describes `DotBoxd.Kernels.Serialization.Json` as JSON IR importer, host import extensions, and plugin package JSON upload helpers, but does not mention module export.
- `README.md:51` says `DotBoxd.Kernels.Serialization.Json` is for `ImportJsonAsync` and `DotBoxdJsonImporter`, again omitting `DotBoxdJsonExporter`.
- `scripts/check-package-consumer-smoke.ps1:144` through `scripts/check-package-consumer-smoke.ps1:147` compile-check `DotBoxdJsonImporter`, `PluginPackageJsonSerializer`, and IPC types, but do not reference or call `DotBoxdJsonExporter.Export(...)`.

## Suggested test or smoke

Extend the package consumer smoke or add a docs-smoke fixture that constructs a small `SandboxModule`, calls `DotBoxdJsonExporter.Export(module)`, imports the exported JSON with `DotBoxdJsonImporter` or `ImportJsonAsync`, and validates that the round-tripped module can be prepared. The fixture should use the public package references and namespaces documented for consumers.

## Suggested fix direction

Update README package guidance and common namespaces to name `DotBoxdJsonExporter` as part of `DotBoxd.Kernels.Serialization.Json`. Add a short round-trip snippet near the JSON IR example or public API docs, then wire that snippet into release smoke so exporter package placement remains covered.

## Scope boundaries

This does not change export behavior or allocation characteristics; `PAL-0012` already tracks exporter allocation performance. This also does not replace the versioned JSON schema work tracked separately.

## Deduplication key

`api/json/module-exporter/package-guidance-smoke`
