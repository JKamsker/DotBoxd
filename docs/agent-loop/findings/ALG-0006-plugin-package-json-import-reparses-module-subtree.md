---
id: ALG-0006
area: perf_algorithm
status: open
priority: medium
title: Plugin package JSON import reparses module subtree
dedup_key: algorithm/plugin-package-json/import/module-subtree-reparse
created_at: 2026-06-12T22:02:48.1967005+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:02:48.1967005+00:00
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

# ALG-0006: Plugin package JSON import reparses module subtree

## Claim

Plugin package JSON import parses the full package, extracts the `module` subtree as raw JSON text, and then sends that raw text through the standalone module importer, causing an additional large string allocation plus a second validation/parse/source-map pass over the module payload.

## Evidence

- `src/SafeIR.Serialization.Json/PluginPackageJsonSerializer.cs` parses the complete package with `JsonDocument.Parse` in `Import`.
- `ReadPackage` then calls `Required(element, "module").GetRawText()` to materialize the module subtree as a new JSON string.
- That string is passed to `SafeIrJsonImporter.Import`, which runs `JsonImportBudgetGuard.Validate`, parses another `JsonDocument`, and builds the module source map again in `src/SafeIR.Serialization.Json/SafeIrJsonImporter.cs`.
- Existing `ALG-0001` covers inefficient source-map construction inside the module importer. This finding is the package-level wrapper doing an avoidable second module parse/copy before that importer work even begins.
- Current `benchmarks/SafeIR.Benchmarks/Json/JsonImportBenchmarks.cs` covers raw module import, but there is no package import/export benchmark that includes manifest/live settings plus a large embedded module.

## Impact

Large plugin packages pay full-package parse cost plus an extra raw-text allocation and module parse proportional to module JSON size. Hosts that load many plugin packages at startup or hot-reload plugins repeatedly will see avoidable CPU and allocation before validation and preparation begin.

## Better target

Import the module directly from the existing `JsonElement` tree, or split `SafeIrJsonImporter` so package import can reuse module-reading logic without `GetRawText()` and without reparsing. If source spans are required, preserve package-level offsets or build a source map once for the full package.

## Benchmark/allocation test idea

Add a BenchmarkDotNet package serialization benchmark that generates plugin packages with 100, 1,000, and 10,000 module statements plus live settings/subscriptions. Measure `PluginPackageJsonSerializer.Import` allocated bytes and elapsed time, and compare package import against direct `SafeIrJsonImporter.Import` of the same module payload to expose the wrapper overhead.
