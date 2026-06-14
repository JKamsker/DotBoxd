---
id: CMP-0012
area: completeness
status: open
priority: medium
title: Public JSON ingestion lacks a versioned schema artifact
dedup_key: completeness/json-ingestion/versioned-schema-artifact-missing
created_at: 2026-06-12T22:17:24.0157886+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T22:17:24.0157886+00:00
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

# CMP-0012: Public JSON ingestion lacks a versioned schema artifact

## Claim

DotBoxd.Kernels's public JSON module and plugin-package upload boundary does not ship a versioned machine-readable schema artifact, even though JSON is the documented ingestion path and the importer enforces a strict shape in code.

## Evidence

- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:427` exposes `DotBoxdJsonImporter.Import(string json)` and `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:440` exposes `PluginPackageJsonSerializer.Import(string json)` as public JSON ingestion APIs.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:452` says JSON import is the only built-in text ingestion path for Safe IR.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/03-architecture.md:113` calls out JSON schema boundary checks as part of the JSON addon architecture.
- `src/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonImporter.cs:34` hard-codes the allowed top-level module fields, and the importer repeats strict `RequireAllowedProperties` checks for functions, statements, expressions, and types throughout that file.
- `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:166` hard-codes plugin package fields, and `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:178`/`:238`/`:299`/`:307` enforce manifest, live-setting, subscription, and entrypoint shapes.
- A repository search for `*.schema.json` and `*schema*.json` found no checked-in JSON Schema file outside build artifacts.
- Existing tests exercise importer behavior, but they do not produce a consumable schema artifact for plugin authors, admin UIs, upload validators, or package tooling.

## Impact

Consumers have to infer the accepted JSON envelope from examples, tests, or importer source. That makes it harder to validate plugin uploads before sending them to a server, build editor/admin forms for package manifests, detect drift between docs and implementation, or version the JSON contract independently from C# package APIs.

## Better target

Publish versioned JSON Schema artifacts for the Safe IR module envelope and plugin package envelope, link them from README/addendum docs, and keep them aligned with importer/exporter behavior. The schemas should include required fields, allowed properties, literal/type/operator shapes, manifest live-setting ranges, entrypoint names, and the module/package version fields.

## Release gate idea

Add a schema drift test that exports representative modules/packages, validates them against the checked-in schemas, and verifies importer strict-shape tests stay synchronized with schema allowed-property lists. Release readiness should fail if JSON importer/exporter shape changes without updating the schema version or schema files.
