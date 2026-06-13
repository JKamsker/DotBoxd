---
id: API-0004
area: api_coherence
status: fixed_pending_verification
priority: medium
title: Public packages do not generate XML documentation files
dedup_key: api/package-xml-docs/not-generated-for-public-packages
created_at: 2026-06-12T22:08:10.8259915+00:00
created_by: continuous-completeness-producer
created_commit: 
updated_at: 2026-06-13T06:08:21.5139129+00:00
claimed_by: worker
claimed_at: 2026-06-13T06:01:42.5815099+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T06:08:21.5139129+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0004: Public packages do not generate XML documentation files

## Claim

The packable public SafeIR packages do not enable XML documentation file generation. As a result, NuGet consumers do not get IntelliSense/API reference XML from the packages, and CI cannot catch missing public XML docs for the stable package surface.

## Why this matters

SafeIR is security-sensitive and exposes host-facing policy, execution, plugin, transport, and verifier APIs. Consumers need package-local API docs for error semantics, safe defaults, and capability boundaries without reverse-engineering source or reading the full spec tree.

## Evidence

- `Directory.Build.props:15` through `Directory.Build.props:17` centralizes package metadata and readme packaging, but does not set `GenerateDocumentationFile` or `DocumentationFile`.
- `rg -n "<Description>|<PackageTags>|<PackageReadmeFile>|GenerateDocumentationFile|DocumentationFile|<TargetFramework>|<IncludeBuildOutput>|<VersionSuffix>" Directory.Build.props src -g "*.csproj"` returned no `GenerateDocumentationFile` or `DocumentationFile` settings for any `src/SafeIR.*` project.
- Public package project files such as `src/SafeIR.Core/SafeIR.Core.csproj:4`, `src/SafeIR.Hosting/SafeIR.Hosting.csproj:13`, `src/SafeIR.Serialization.Json/SafeIR.Serialization.Json.csproj:10`, `src/SafeIR.Plugins/SafeIR.Plugins.csproj:10`, and `src/SafeIR.Transport.Http/SafeIR.Transport.Http.csproj:9` only declare target framework/build settings in their project-local metadata.
- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md` documents a broad public surface, including host creation, policy building, execution options/results, JSON import/export, plugin JSON install, verifier, worker, and error model APIs, but those docs are not emitted as package XML documentation.

## Suggested acceptance test

Add a package metadata/packaging test that packs the product projects and asserts every public runtime package includes the expected XML documentation entry next to its DLL, for example `lib/net10.0/SafeIR.Core.xml`. Decide and document the analyzer package expectation separately because `SafeIR.PluginAnalyzer` packs under `analyzers/dotnet/cs`.

## Suggested fix direction

Enable XML documentation generation for packable public projects, either centrally in `Directory.Build.props` with analyzer-specific handling or per project where needed. Add or require XML summaries for the stable public surface and suppress only intentional internal/generated gaps.

## Scope boundaries

Do not redesign APIs as part of this finding. This is about package documentation emission and coverage for the existing public surface.

## Deduplication key

`api/package-xml-docs/not-generated-for-public-packages`
