---
id: API-0009
area: api_coherence
status: fixed_pending_verification
priority: high
title: Public packages lack an API compatibility baseline gate
dedup_key: api/package-release/public-api-compatibility-baseline-missing
created_at: 2026-06-12T22:16:43.7897045+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T00:25:13.4335052+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:22:52.1031063+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:25:13.4335052+00:00
fixed_commit: pending
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0009: Public packages lack an API compatibility baseline gate

## Claim

The public DotBoxd.Kernels package set has no public API compatibility baseline or release gate, so accidental breaking changes to shipped types can pass CI as long as the source tree still builds and package metadata is valid.

## Evidence

- `Directory.Build.props` sets shared package metadata and versioning, but it does not enable .NET package validation or API compatibility checks such as `EnablePackageValidation`, `ApiCompatValidateAssemblies`, or a baseline package version.
- The product `.csproj` files under `src/` define target frameworks and references, but they do not opt into API compatibility validation or reference public API analyzer baselines.
- A repository search found no `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt` files outside build output, so public surface changes are not tracked through a source-controlled API baseline.
- A repository search for `PublicApiAnalyzers`, `Microsoft.DotNet.ApiCompat`, `EnablePackageValidation`, and `ApiCompatValidateAssemblies` found no DotBoxd.Kernels package validation configuration.
- `.github/workflows/ci.yml:98` packs the solution and `.github/workflows/ci.yml:102`/`:119` run `scripts/check-package-metadata.ps1`, but those checks validate metadata/prerelease policy rather than comparing the package API surface against a prior baseline.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:576` describes a stabilized minimum surface, but there is no automated gate that prevents removing or changing that surface accidentally.

## Impact

DotBoxd.Kernels exposes many public NuGet packages (`DotBoxd.Kernels`, `DotBoxd.Hosting`, `DotBoxd.Plugins`, transport addons, JSON serialization, compiler/verifier APIs). Without an API compatibility gate, a public constructor, method, enum member, record property, analyzer package asset, or namespace can be removed or changed unintentionally and still pass the current release pipeline. Consumers discover the break only after upgrading packages.

## Better target

Add a public API compatibility gate for packable packages. Viable approaches include .NET package validation with a checked-in/package baseline, PublicApiAnalyzers `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt` files per assembly, or an equivalent ApiCompat job that compares release packages to the last stable package version. Document how intentional breaking changes are approved and versioned.

## Release gate idea

Extend CI/release readiness so release branches and tags fail when a public package introduces an API break without an explicit baseline update or documented major-version decision. Keep analyzer package handling explicit because it ships analyzer assets rather than ordinary `lib` runtime assets.
