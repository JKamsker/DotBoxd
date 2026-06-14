---
id: API-0022
area: api_coherence
status: fixed_pending_verification
priority: high
title: Public API baseline gate records members of internal types
dedup_key: api/package-release/api-baseline/effective-public-accessibility
created_at: 2026-06-13T06:30:59.9966235+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T07:49:30.8497771+00:00
claimed_by: fixer
claimed_at: 2026-06-13T07:49:30.7141931+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-13T07:49:30.8497771+00:00
fixed_commit: b14fd0a
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0022: Public API baseline gate records members of internal types

## Claim

The public API compatibility baseline gate records any declaration line that starts with `public` or `protected`, even when the declaration is inside an `internal` containing type. As a result, checked-in `docs/api-baselines` files include implementation-only members from internal compiler and analyzer helpers as if they were supported public API.

This is a release validation gap separate from `API-0009`: the baseline gate now exists, but it does not measure effective public API accessibility accurately.

## Why this matters

False public API entries make the release gate noisy and misleading. Internal refactors can require baseline updates and versioning explanations even though no NuGet consumer can call the member, while reviewers may miss actual package-surface changes because the baseline is padded with implementation details. This weakens package readiness because the API compatibility evidence no longer cleanly represents the supported consumer contract.

## Evidence

- `scripts/check-api-compat-baseline.ps1` implements API extraction by scanning raw `.cs` text. `Normalize-ApiLine(...)` and `Normalize-ApiDeclaration(...)` accept declarations whose own text starts with `public`, `protected internal`, or `protected`, and `Get-PackageApi(...)` feeds them lines from every source file without tracking the accessibility of containing types.
- `src/DotBoxd.Kernels.Compiler/Emitters/MethodEmitter.cs:9` declares `internal sealed class MethodEmitter`, but `docs/api-baselines/DotBoxd.Kernels.Compiler.txt:34` records its public constructor as baseline API.
- `src/DotBoxd.Kernels.Compiler/IlEmitterPrimitives.cs:8` declares `internal static class IlEmitterPrimitives`, but `docs/api-baselines/DotBoxd.Kernels.Compiler.txt:52` records `public static MethodInfo Runtime(string name)`.
- `src/DotBoxd.Kernels.Compiler/Internal/PersistentCompiledArtifactCacheValidator.cs:6` declares an internal validator type, but `docs/api-baselines/DotBoxd.Kernels.Compiler.txt:73` records `public static void ValidateManifest(...)`.
- `src/DotBoxd.Plugins.Analyzer/Analysis/EquatableArray.cs:5` declares `internal readonly struct EquatableArray<T>`, but `docs/api-baselines/DotBoxd.Plugins.Analyzer.txt:121` records its public constructor and nearby lines record its public members.
- `src/DotBoxd.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxdExpressionLoweringContext.cs:5` declares an internal lowering context, but `docs/api-baselines/DotBoxd.Plugins.Analyzer.txt:133` records its constructor and later baseline entries expose lowering methods that take it.

## Suggested test or gate

Add a focused regression for `scripts/check-api-compat-baseline.ps1` with a fixture containing an `internal class` that has public members plus a real public type. The generated API list should include only the effectively public type/member surface. Include nested types and public members inside internal records/structs so analyzer/compiler-style helpers stay covered.

## Suggested fix direction

Replace the text-only extractor with a Roslyn symbol walk, `dotnet publicapi`, `Microsoft.DotNet.ApiCompat`, PublicApiAnalyzers, or an equivalent implementation that filters by effective accessibility through containing symbols. After fixing the extractor, regenerate baselines so internal helper entries disappear, and document how analyzer packages are handled because they ship analyzer assets rather than normal `lib` assemblies.

## Scope boundaries

Do not remove the API baseline gate. Do not broaden package public APIs to match the bad baseline. This finding is only about making the release gate measure the real public package contract.

## Deduplication key

`api/package-release/api-baseline/effective-public-accessibility`

## Verification checklist

- [ ] The API extraction ignores public members whose containing type is not effectively public.
- [ ] Regression coverage proves internal compiler/analyzer helpers are excluded.
- [ ] `docs/api-baselines/*.txt` are regenerated from the corrected extractor.
- [ ] The API compatibility check still fails on a real supported public API addition/removal.
