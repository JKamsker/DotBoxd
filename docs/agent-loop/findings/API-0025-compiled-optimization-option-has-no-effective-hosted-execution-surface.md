---
id: API-0025
area: api_coherence
status: open
priority: medium
title: Compiled optimization option has no effective hosted execution surface
dedup_key: api/compiler/compile-options-optimize-not-effective
created_at: 2026-06-13T06:44:39.0326736+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T06:44:39.0326736+00:00
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

# API-0025: Compiled optimization option has no effective hosted execution surface

## Problem

`CompileOptions` exposes an `Optimize` flag as public compiler API, and the compiler records that flag in cache keys and artifact manifests, but hosted compiled execution never exposes or sets the flag and the reflection compiler does not use it to change emission.

As a result, `Optimize = true` is a package-facing option with identity/audit consequences but no effective execution implementation. It creates distinct cache keys and `OptimizationFlags` values without producing a distinct optimized compiled runtime.

## Affected public API

- `src/DotBoxd.Kernels.Compiler/CompilerContracts.cs`
- `src/DotBoxd.Kernels.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs`
- `src/DotBoxd.Hosting/Execution/SandboxHost.cs`
- `src/DotBoxd.Kernels/ExecutionPlan.cs`

## Evidence

- `src/DotBoxd.Kernels.Compiler/CompilerContracts.cs:8` declares `public sealed record CompileOptions(string Entrypoint, bool Optimize = false)`.
- `src/DotBoxd.Kernels.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs:32`, `:60`, `:194`, and `:206` use `options.Optimize` only for cache-key/manifest identity and the string flag `"opt"` versus `"boxed-values"`.
- The actual emission path, `EmitAssembly(plan, function)`, receives no optimize argument and the method body has no optimization branch.
- `src/DotBoxd.Hosting/Execution/SandboxHost.cs:234` always calls `_compiler.CompileAsync(plan, new CompileOptions(entrypoint), ...)`, so the primary public host execution API cannot request optimized compilation.
- `src/DotBoxd.Kernels/ExecutionPlan.cs:89` defines `SandboxExecutionOptions` without any compile optimization option.

## Impact

Package consumers see a public optimization switch and artifact metadata that claims an optimized form, but normal hosted execution cannot request it and direct compiler consumers get the same emitted implementation under a different cache identity. That weakens the completeness of the compiled backend contract and can mislead cache/audit tooling that treats `OptimizationFlags` as evidence of a different runtime form.

## Recommendation

Either implement and expose the feature end to end, or remove/de-scope it from the public contract until it is real:

- Implement an optimized emission path, thread an option through hosted execution, and add tests that prove `Optimize = true` affects the artifact or a supported optimization invariant.
- Or remove the public flag/`"opt"` manifest state, or mark it internal/unsupported, so public cache keys and manifests only describe implemented compiled runtime forms.

## Non-duplicates checked

Existing compiled-cache findings cover cache validation cost, persistent cache verification, manifest mutability, verifier identity, cache origin, and runnable cache examples. None track that the public `CompileOptions.Optimize` feature itself is not reachable through hosted execution and does not alter compiler output.

## Acceptance criteria

- [ ] The public compiler option surface no longer advertises an unimplemented optimization mode.
- [ ] If optimization remains public, hosted compiled execution has an explicit way to request it.
- [ ] Tests distinguish the supported optimized and non-optimized paths, or assert that only the implemented non-optimized path is exposed.
