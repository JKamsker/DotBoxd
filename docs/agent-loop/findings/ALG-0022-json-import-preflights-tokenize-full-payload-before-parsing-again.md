---
id: ALG-0022
area: perf_algorithm
status: open
priority: medium
title: JSON import preflights tokenize full payload before parsing again
dedup_key: algorithm/json-import/preflight-tokenizes-before-parse
created_at: 2026-06-13T07:03:38.3225891+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T07:03:38.3225891+00:00
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

# ALG-0022: JSON import preflights tokenize full payload before parsing again

## Claim

JSON module and plugin package import perform a full UTF-8 encoding/token scan in `JsonImportBudgetGuard` before the real `JsonDocument` parse, then module import builds a source map with another UTF-8 encoding/token scan over the same text. Large imports therefore still pay multiple full-payload passes even after the raw-text source-map search issue was fixed.

## Evidence

- `src/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonImporter.cs:12` calls `JsonImportBudgetGuard.Validate(json)` before `JsonDocument.Parse` at `src/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonImporter.cs:13`, then always calls `JsonSourceMap.Create(json, document.RootElement)` at `src/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonImporter.cs:20`.
- `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:25` and `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:26` repeat the same preflight-then-parse pattern for package JSON before the package importer reaches the embedded module path.
- `src/DotBoxd.Kernels.Serialization.Json/Internal/JsonImportBudgetGuard.cs:19` encodes the entire input string with `Encoding.UTF8.GetBytes(json)`, and `src/DotBoxd.Kernels.Serialization.Json/Internal/JsonImportBudgetGuard.cs:30` scans that byte array with `Utf8JsonReader` only to enforce import limits.
- `src/DotBoxd.Kernels.Serialization.Json/Internal/JsonSourceMap.cs:56` encodes the module JSON to UTF-8 again, and `src/DotBoxd.Kernels.Serialization.Json/Internal/JsonSourceMap.cs:57` creates another `Utf8JsonReader` to collect token positions for spans.
- Existing `ALG-0001` covered the former raw-text source-map search algorithm, `ALG-0006` covers package-level module subtree reparsing, and `PAL-0028` covers per-object schema allocation. This finding is the remaining current full-payload preflight and source-map tokenization work.

## Impact

Generated modules near the 1 MB / 100,000-token import cap pay at least a budget UTF-8 allocation plus token scan, the `JsonDocument` parse, a source-map UTF-8 allocation plus token scan, and then model construction traversal. Plugin package import can add the package-level preflight and parse before module import repeats its own passes. Startup, hot-reload, analyzer package ingestion, and CI/package validation paths all see avoidable latency and allocation proportional to full JSON size.

## Better target

Keep the fail-closed import limits, but avoid independent full-payload passes. Parse from one UTF-8 buffer, combine budget counting and source-span token capture in a single reader pass, or expose importer internals that can reuse parser token offsets/source-map data without re-encoding the same string. The target should be one bounded pre-parse buffer/scan, not separate UTF-8 allocations and token readers for budget and spans.

## Benchmark/allocation test idea

Extend `benchmarks/DotBoxd.Kernels.Benchmarks/Json/JsonImportBenchmarks.cs` with generated modules and plugin packages at 100, 1,000, and 10,000 statements plus metadata/live settings. Measure elapsed time and allocated bytes in `DotBoxdJsonImporter.Import` and `PluginPackageJsonSerializer.Import`, and assert steady-state import does not allocate multiple full UTF-8 buffers or run multiple full token scans for the same JSON payload.
