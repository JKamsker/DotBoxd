---
id: API-0007
area: api_coherence
status: fixed_pending_verification
priority: medium
title: NuGet package readme lacks install and package-composition guidance
dedup_key: api/nuget-readme/missing-install-package-composition-guidance
created_at: 2026-06-12T22:12:14.6495604+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T06:08:25.2526843+00:00
claimed_by: worker
claimed_at: 2026-06-13T06:01:45.9617307+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T06:08:25.2526843+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0007: NuGet package readme lacks install and package-composition guidance

# NuGet package readme lacks install and package-composition guidance

## Claim

All packable DotBoxd.Kernels packages publish the repository `README.md` as their NuGet readme, but that readme does not tell package consumers which `dotnet add package` commands or package combinations are required for the advertised public surfaces. It lists package roles and shows source-tree validation/examples, but it never provides install guidance for a fresh NuGet consumer.

## Why this matters

DotBoxd.Kernels is split into many packages with optional addons: hosting, JSON import/export, HTTP transport, plugin APIs, plugin analyzer generation, and the preview DotBoxd IPC transport. Users consuming from NuGet need a small installation matrix that maps tasks to packages and namespaces. Without it, the package readme sends users back to repository-local commands and project-reference examples, while package-specific setup remains implicit.

## Evidence

- `Directory.Build.props:17` sets `<PackageReadmeFile>README.md</PackageReadmeFile>` for all packages.
- `Directory.Build.props:24` packs the repository root `README.md` into every package.
- `README.md:7` through `README.md:19` lists package roles, but it does not include `dotnet add package` commands or a package-composition table for common tasks.
- `README.md:22` through `README.md:51` shows minimal host usage that requires multiple packages/namespaces, but the setup immediately before it does not say which NuGet packages to install for that snippet.
- `README.md:80` through `README.md:111` documents repository-local restore/build/test/pack validation, and `README.md:120` through `README.md:143` documents running examples from repository paths, not consuming the published packages.
- `scripts/check-package-metadata.ps1` only requires that each package declares and contains `README.md`; it does not check that the package readme contains install or package-selection guidance.
- Existing package metadata findings cover generic description/tags and XML docs. This finding is specifically about the user-facing NuGet readme content that every package currently publishes.

## Suggested acceptance test

Extend package/readme validation to require an install guidance section in the packed readme. At minimum, the section should show package installation for:

- Core host execution: `DotBoxd.Hosting`, `DotBoxd.Kernels.Runtime`, and `DotBoxd.Kernels.Serialization.Json` as needed by the README host snippet.
- HTTP transport: `DotBoxd.Hosting.Http` plus the public addon namespace.
- Plugin development: `DotBoxd.Plugins.Analyzer` as an analyzer package plus `DotBoxd.Plugins`.
- Production plugin JSON upload: `DotBoxd.Kernels.Serialization.Json` with `DotBoxd.Plugins`.
- IPC preview: `DotBoxd.Pushdown.Services` with explicit preview/prerelease wording.

## Suggested fix direction

Add a `## Installing from NuGet` section to `README.md`, or switch to package-specific readmes if per-package guidance is preferred. The guidance should include `dotnet add package` commands, the required `using` namespaces, and short task-to-package mapping so consumers can reproduce the documented snippets without inspecting project files.

## Scope boundaries

Do not change package IDs, dependencies, or public APIs as part of this finding. This is only about making the packaged readme complete for NuGet consumers.

## Deduplication key

`api/nuget-readme/missing-install-package-composition-guidance`
