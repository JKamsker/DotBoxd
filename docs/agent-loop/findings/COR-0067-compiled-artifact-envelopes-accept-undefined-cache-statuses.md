---
id: COR-0067
area: correctness
status: open
priority: medium
title: Compiled artifact envelopes accept undefined cache statuses
dedup_key: correctness/compiled-artifact/cache-status/undefined-enum
created_at: 2026-06-13T06:44:32.5888626+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T06:44:32.5888626+00:00
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

# COR-0067: Compiled artifact envelopes accept undefined cache statuses

## Claim

Compiled artifact envelopes accept undefined `CompiledCacheStatus` values and later publish those impossible statuses in run-summary audit telemetry.

## Evidence

- `src/DotBoxd.Kernels.Compiler/CompilerContracts.cs` defines the finite `CompiledCacheStatus` enum (`None`, `Hit`, `Miss`, `Invalid`, `Recompiled`) and validates `runtimeForm` in the `CompiledArtifact` constructor, but the same constructor assigns `CacheStatus = cacheStatus` without an `Enum.IsDefined` check. The public `CacheStatus` init property also allows `artifact with { CacheStatus = (CompiledCacheStatus)123 }`.
- `src/DotBoxd.Hosting/Execution/CompiledArtifactGuard.cs` revalidates `artifact.RuntimeForm` before execution, but it never validates `artifact.CacheStatus`. The materialization path preserves the supplied status, and `CompiledExecutableCache.WithCurrentMetadata(...)` copies `current.CacheStatus` back onto cached materializations.
- `src/DotBoxd.Hosting/Execution/CompiledExecutionRunner.cs` converts `artifact.CacheStatus.ToString()` into the public `RunSummary` message and field dictionary. An undefined enum value therefore becomes audit evidence such as `cacheStatus=123` even though no such cache state exists.

## Impact

A custom compiler, bad persistent-cache integration, or mutated public `CompiledArtifact` can produce a validated and executed compiled result with impossible cache telemetry. Consumers that inspect run summaries, plugin execution observations, or cache diagnostics can make decisions from malformed state that the host should have rejected at the artifact boundary.

## Better target

Validate `CompiledCacheStatus` anywhere a `CompiledArtifact` is constructed or accepted for execution, including `CompiledArtifactGuard.ValidateExecutableEnvelope(...)`. Add a regression test that builds a valid artifact, changes `CacheStatus` to an undefined enum value, and asserts compiled execution fails closed instead of publishing the malformed status.

## Deduplication key

`correctness/compiled-artifact/cache-status/undefined-enum`
