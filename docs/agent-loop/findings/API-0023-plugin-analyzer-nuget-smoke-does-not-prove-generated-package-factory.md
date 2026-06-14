---
id: API-0023
area: api_coherence
status: open
priority: medium
title: Plugin analyzer NuGet smoke does not prove generated package factory
dedup_key: api/plugin-analyzer/nuget-source-generation-smoke
created_at: 2026-06-13T06:32:24.7438685+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T06:32:24.7438685+00:00
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

# API-0023: Plugin analyzer NuGet smoke does not prove generated package factory

## Claim

`DotBoxd.Plugins.Analyzer` is documented as the source-generator package that turns `[GamePlugin]` / `IEventKernel<TEvent>` kernels into plugin package factories, but the package-backed release smoke only references the analyzer package. It does not compile a consumer kernel or prove that the NuGet analyzer asset generates a callable `*PluginPackage.Create()` factory.

This is distinct from analyzer diagnostic documentation (`API-0008`) and custom host binding examples (`CMP-0022`): the missing proof is the basic NuGet source-generation path for the public plugin package authoring surface.

## Why this matters

A broken analyzer package layout, missing analyzer dependency, wrong `analyzers/dotnet/cs` asset, or generator initialization regression could still pass the current package consumer smoke as long as a project can reference `DotBoxd.Plugins.Analyzer`. Plugin authors would discover the failure only when following the public addendum pattern and expecting the generated factory to appear in their build.

## Evidence

- `README.md:21` lists `DotBoxd.Plugins.Analyzer` as the source generator and analyzer for local plugin packages, and `README.md:42` tells consumers to `dotnet add package DotBoxd.Plugins.Analyzer`.
- `docs/Specs/Addendum/Examples.md:48` states that the current source generator lowers `IEventKernel<TEvent>` kernels into plugin packages.
- `docs/Specs/Addendum/Examples.md:55` through the surrounding sample use `[GamePlugin("fire-damage")]` on `FireDamageKernel`, and `docs/Specs/Addendum/Examples.md:117` installs the generated `FireDamagePluginPackage.Create()` factory.
- The runnable examples that exercise generated factories wire the analyzer through source-tree project references, not the NuGet package: `examples/Addendum/DotBoxd.Kernels.AddendumExamples/DotBoxd.Kernels.AddendumExamples.csproj:4` and `examples/LocalPlugin/DotBoxd.Kernels.PluginLocal/DotBoxd.Kernels.PluginLocal.csproj:4` both reference `src/DotBoxd.Plugins.Analyzer` as an analyzer project reference.
- `scripts/check-package-consumer-smoke.ps1:116` references the packaged `DotBoxd.Plugins.Analyzer`, but its generated `Program.cs` only compile-checks package types such as `DotBoxdJsonImporter`, `PluginPackageJsonSerializer`, and `DotBoxdDotBoxdRpcMessagePackIpc`; it does not define a `[GamePlugin]` kernel, implement `IEventKernel<TEvent>`, or call a generated `*PluginPackage.Create()` member.

## Suggested test or smoke

Extend `scripts/check-package-consumer-smoke.ps1` to write a tiny consumer plugin kernel and event type into the temporary project, reference `DotBoxd.Plugins` plus the packaged `DotBoxd.Plugins.Analyzer`, build it, and call the generated factory from `Program.cs`. The smoke should fail if the analyzer asset is missing, the generator does not run from the package, or the generated factory cannot create a valid `PluginPackage`.

## Suggested fix direction

Add the package-backed generator proof to the existing package consumer smoke or create a dedicated package-backed plugin authoring smoke project. Keep the source-tree examples as examples, but make release validation prove the NuGet package path documented in README.

## Scope boundaries

Do not change plugin runtime behavior or source generator semantics as part of this finding. Do not duplicate the custom host binding authoring example tracked separately; a minimal built-in event-kernel package factory is enough to prove the package path.

## Deduplication key

`api/plugin-analyzer/nuget-source-generation-smoke`

## Verification checklist

- [ ] A package-backed smoke project defines a `[GamePlugin]` kernel implementing `IEventKernel<TEvent>`.
- [ ] The smoke references `DotBoxd.Plugins.Analyzer` from the packed NuGet output, not a project reference.
- [ ] The smoke calls the generated `*PluginPackage.Create()` factory and observes a valid `PluginPackage` shape.
- [ ] Existing local source-tree examples still build and run.
