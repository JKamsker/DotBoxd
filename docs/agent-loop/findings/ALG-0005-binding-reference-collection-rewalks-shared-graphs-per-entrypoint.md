---
id: ALG-0005
area: perf_algorithm
status: verified
priority: medium
title: Binding reference collection rewalks shared graphs per entrypoint
dedup_key: algorithm/binding-reference/per-entrypoint-graph-rewalk
created_at: 2026-06-12T21:01:53.3117137+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T21:52:34.4453536+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:43:43.2753205+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:49:19.4059355+00:00
fixed_commit: 
verified_by: independent-verifier
verified_at: 2026-06-12T21:52:34.4453536+00:00
verified_commit: 
duplicate_of: 
---

# ALG-0005: Binding reference collection rewalks shared graphs per entrypoint

## Claim

Binding reference collection rebuilds the function dictionary and rewalks reachable function graphs separately for each entrypoint and execution check, causing repeated graph traversal work on modules with shared helper functions.

## Evidence

- `src/DotBoxd.Kernels/Bindings/BindingReferenceCollector.cs:11` builds a fresh function dictionary for every `Collect` call.
- `src/DotBoxd.Kernels/Bindings/BindingReferenceCollector.cs:16` creates a fresh visited set for an entrypoint-specific traversal.
- `src/DotBoxd.Kernels/Bindings/BindingReferenceCollector.cs:21` also creates a new visited set for every function when collecting all references without an entrypoint.
- `src/DotBoxd.Kernels.Validation/ModuleValidator.cs:36` loops over every entrypoint and calls `BindingReferenceCollector.Collect(module, bindings, function.Id)` for each one while computing required capabilities.
- `src/DotBoxd.Hosting/Execution/SandboxHost.Capabilities.cs:43` repeats binding reference collection when checking revoked capabilities for an entrypoint.
- `src/DotBoxd.Hosting/Execution/CompiledExecutionRunner.cs:20` repeats binding reference collection at compiled execution dispatch to create allowed bindings.
- The benchmark project has no module-validation or execution-dispatch benchmark that scales entrypoint count, helper-function sharing, or binding-call density.

## Impact

A module with many entrypoints that all call the same helper graph repeatedly rebuilds the same dictionary and revisits the same functions/binding calls. Validation and dispatch work can grow with entrypoints times reachable graph size even though the module-level call graph and per-entrypoint binding sets are stable after prepare.

## Better target

Compute a module call graph and binding-reference summary once during validation or execution-plan construction. Cache per-entrypoint binding IDs/capabilities on `ExecutionPlan` so validation, revocation checks, and compiled dispatch can reuse the same immutable sets.

## Benchmark idea

Add a BenchmarkDotNet validation/dispatch benchmark that generates modules with 1, 10, and 100 entrypoints sharing a helper chain with binding calls. Measure `ModuleValidator.Validate`, `SandboxHost.PrepareAsync`, revoked-capability checks, and compiled dispatch setup allocations before and after caching per-entrypoint binding references.
