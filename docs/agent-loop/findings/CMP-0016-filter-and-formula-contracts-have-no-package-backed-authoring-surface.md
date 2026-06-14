---
id: CMP-0016
area: completeness
status: open
priority: medium
title: Filter and formula contracts have no package-backed authoring surface
dedup_key: completeness/addendum/simple-filter-formula/no-package-backed-lowering-surface
created_at: 2026-06-12T23:03:05.0028552+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T23:03:05.0028552+00:00
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

# CMP-0016: Filter and formula contracts have no package-backed authoring surface

## Claim

The addendum presents simple filters and formulas as shared plugin contracts, but the current user-facing DotBoxd.Kernels package authoring path only lowers `IEventKernel<TEvent>` kernels. The runnable simple filter/formula example instantiates the C# implementations directly and calls them as normal host objects, so it does not prove that `IItemFilter` or `IDamageFormula` can be uploaded, validated, lowered to Safe IR, installed, or executed through the plugin package boundary.

## Why this matters

A plugin author can reasonably read the addendum walkthrough as a supported progression from simple filters/formulas to kernels. Today only the kernel path has a package-backed DotBoxd.Kernels surface. That leaves a feature-completeness gap: the docs and examples include non-event contracts, but users cannot build or ship those contracts through the same DotBoxd.Kernels upload/install path without inventing their own lowering/adapter model.

## Evidence

- `docs/Specs/Addendum/Addendum.md:82` documents `IItemFilter`, and `docs/Specs/Addendum/Addendum.md:87` documents `IDamageFormula` as shared plugin-slot interfaces.
- `docs/Specs/Addendum/Addendum.md:148` states that the current SDK generator lowers `IEventKernel<TEvent>` kernels and that other shared interfaces are only contract guidance until a host provides a matching adapter/lowering path.
- `docs/Specs/Addendum/Examples.md:17` starts the public walkthrough with "Implement A Simple Filter", while `docs/Specs/Addendum/Examples.md:47` again says the current generator only lowers `IEventKernel<TEvent>` kernels into plugin packages.
- `examples/Addendum/README.md:12` lists "simple filters and formulas" as runnable addendum examples in `SimpleContractExamples.cs`.
- `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/SimpleContractExamples.cs:21` creates `new EpicItemsOnly()` and `new ArmorAdjustedDamageFormula()` directly, then calls `Accept`/`Calculate` as ordinary C# methods. It does not export a package, import JSON, install into `PluginServer`, or execute via `SandboxHost`.
- `examples/PluginIpc/DotBoxd.Kernels.PluginIpc.Server.Abstractions/ItemContracts.cs:3` and `examples/PluginIpc/DotBoxd.Kernels.PluginIpc.Server.Abstractions/FormulaContracts.cs:3` define the contracts, but the exposed package/generator path in this repo is event-kernel based.

This is distinct from the existing event-kernel findings: `CMP-0006` covers implicit event adapter discovery, `CMP-0007` covers production JSON upload coverage, and `CMP-0010` covers manifest inspection details. This finding is specifically about the non-event filter/formula contract category having no package-backed user-facing surface.

## Suggested test or benchmark

Add a package-backed completeness smoke test for one non-event contract, for example an `IItemFilter` package that is generated or adapted into Safe IR, exported to JSON, installed through the public server/upload boundary, and invoked through a public filter/formula execution API. If non-event contracts are intentionally out of scope for the release, add a docs-smoke assertion that the simple filter/formula walkthrough is clearly labeled host-side guidance and is not presented as a runnable DotBoxd.Kernels plugin package feature.

## Suggested fix direction

Either provide a small explicit lowering/adapter surface for simple filters and formulas, with a runnable example that proves upload/install/execute through DotBoxd.Kernels, or split those sections out of the package-backed walkthrough and label them as host-side contract design guidance only. Do not execute arbitrary plugin DLLs or bypass the existing JSON Safe IR package boundary to make the examples pass.

## Scope boundaries

Do not change the event-kernel package flow, plugin-server policy, or IPC sample as part of this finding unless needed to wire a narrowly scoped non-event contract example. Keep any new surface explicit and capability-checked rather than treating arbitrary shared interfaces as executable plugin code by default.

## Deduplication key

`completeness/addendum/simple-filter-formula/no-package-backed-lowering-surface`

## Verification checklist

- [ ] A non-event contract example is either package-backed and runnable through DotBoxd.Kernels, or clearly documented as host-side-only guidance.
- [ ] The public walkthrough no longer implies simple filters/formulas are complete uploadable plugin package features unless they are.
- [ ] The smoke/docs gate catches future drift between documented contract categories and package-backed example coverage.
- [ ] Existing event-kernel plugin behavior remains unchanged.
