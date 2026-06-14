---
id: ALG-0007
area: perf_algorithm
status: open
priority: low
title: Plugin package validation rescans module functions for entrypoints
dedup_key: algorithm/plugin-package-validation/entrypoint-function-rescans
created_at: 2026-06-12T22:07:31.4044262+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:07:31.4044262+00:00
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

# ALG-0007: Plugin package validation rescans module functions for entrypoints

## Claim

Plugin package validation repeatedly searches the module function list for the same kernel entrypoints instead of indexing entrypoint functions once for the package validation/preparation pass.

## Evidence

- `src/DotBoxd.Plugins/Runtime/PluginPackageValidator.cs:27` calls `ValidateEntrypoints`, which validates both configured kernel entrypoints.
- `src/DotBoxd.Plugins/Runtime/PluginPackageValidator.cs:100` checks each entrypoint with `package.Module.Functions.Any(...)`, scanning the function list once for `ShouldHandle` and once for `Handle`.
- `src/DotBoxd.Plugins/Runtime/PluginPreparedPackageValidator.cs:75` later validates prepared entrypoints, and `src/DotBoxd.Plugins/Runtime/PluginPreparedPackageValidator.cs:135` uses `FirstOrDefault(...)` over `package.Module.Functions` for each of those same entrypoint IDs.
- `src/DotBoxd.Plugins/Runtime/PluginPreparedPackageValidator.cs:102` then builds expected parameter shape after the function rescans, so the package path has no shared indexed entrypoint lookup.
- Existing tests cover validation behavior (`tests/DotBoxd.Kernels.Tests/Misc06/PluginPackageValidationTests.cs`) and analyzer package generation, but the benchmark project has no plugin package validation/preparation benchmark that scales module function count.

## Impact

Generated plugin packages can include many helper functions while only two kernel entrypoints are relevant to package validation. Validation currently performs multiple O(function-count) scans for the same IDs, adding avoidable prepare/install latency for large packages and repeated hot-reload installs.

## Better target

Build a dictionary of public entrypoint functions once during package validation or reuse an entrypoint index from the execution plan. Both basic validation and prepared validation should resolve `ShouldHandle` and `Handle` from that index without repeated full-list scans.

## Benchmark/allocation test idea

Add a BenchmarkDotNet plugin package validation benchmark that constructs packages with 10, 100, 1,000, and 10,000 module functions while keeping two kernel entrypoints fixed. Measure `PluginServer.InstallAsync` or the validator path time/allocations, and assert entrypoint resolution scales with entrypoint count rather than module function count.
