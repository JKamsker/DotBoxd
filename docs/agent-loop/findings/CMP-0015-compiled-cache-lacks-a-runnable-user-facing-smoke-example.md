---
id: CMP-0015
area: completeness
status: open
priority: medium
title: Compiled cache lacks a runnable user-facing smoke example
dedup_key: completeness/compiled-cache/runnable-user-facing-smoke-missing
created_at: 2026-06-12T22:58:06.4917429+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T22:58:06.4917429+00:00
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

# CMP-0015: Compiled cache lacks a runnable user-facing smoke example

## Claim

The persistent compiled-cache feature is public and release-tested internally, but there is no runnable user-facing example or docs-smoke path that shows a host how to configure `UseCompilerCache`, exercise a miss followed by a verified hit, and observe the cache status/audit behavior.

## Evidence

- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:12` includes `builder.UseCompilerCache("/var/cache/safe-ir")` in the high-level usage sample, and `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:89` lists `SandboxHostBuilder.UseCompilerCache(string cacheDirectory)` as public API.
- The same public API doc exposes `CompiledCacheStatus`, `PersistentCompiledArtifactCache`, and `CompiledCacheLookup` at `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:319` through `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:414`, so cache behavior is part of the documented host surface.
- Implementation and tests exist: `src/SafeIR.Hosting/Execution/SandboxHostBuilder.cs:70` wires `UseCompilerCache`, `src/SafeIR.Compiler/PersistentCompiledArtifactCache.cs:8` implements the cache, and `tests/SafeIR.Tests/Compiled/Core/CompiledCacheTests.cs:226` exercises host configuration with a cache directory.
- A targeted docs/examples search finds `UseCompilerCache` only in the spec and tests, not in `README.md`, `examples/`, or the addendum examples.
- `scripts/check-docs-smoke.ps1:6` through `scripts/check-docs-smoke.ps1:10` declares only the addendum, local plugin, IPC server, and IPC client example projects, and `scripts/check-docs-smoke.ps1:131` through `scripts/check-docs-smoke.ps1:132` runs only addendum/local plugin before optional IPC smoke. No compiled-cache example is smoked.
- Existing cache findings cover correctness, retention, lock files, quarantine cleanup, and artifact materialization. They do not require a public runnable example that proves the supported cache setup and observable hit/miss behavior for package consumers.

## Impact

Consumers can see that compiled cache is available, but the only executable guidance lives in internal tests. A host integrating this feature must infer cache-directory prerequisites, how to request compiled execution, what a first-run miss and second-run hit look like, and how cache status appears in audit/result telemetry. Because the docs-smoke gate does not run such a path, future changes can break the documented cache setup while README/addendum examples and CI still pass.

## Better target

Add a small public example or docs-smoke mode that creates a safe temporary cache directory, configures `SandboxHostBuilder.UseCompilerCache(...)`, prepares a minimal module, runs compiled execution twice, and prints/asserts the first run as miss/recompiled and the second run as a verified cache hit. Link it from `README.md` or the public API docs and include it in `scripts/check-docs-smoke.ps1` so release validation proves the consumer-facing cache workflow.

## Suggested fix direction

Create an `examples/CompiledCache` project or extend an existing docs-smoke example with an explicit compiled-cache scenario. The example should document cache root requirements, cleanup expectations for temporary/demo roots, and the telemetry fields operators should inspect (`CacheStatus`, `CacheInvalidated`, `RunSummary`, and compiled/runtime mode). Keep internal cache corruption tests separate; this finding is about the supported happy-path user surface.
