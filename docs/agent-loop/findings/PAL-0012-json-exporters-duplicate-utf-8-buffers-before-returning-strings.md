---
id: PAL-0012
area: perf_alloc
status: open
priority: medium
title: JSON exporters duplicate UTF-8 buffers before returning strings
dedup_key: alloc/json-export/memorystream-toarray-string-copy
created_at: 2026-06-12T22:07:28.3411034+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:07:28.3411034+00:00
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

# PAL-0012: JSON exporters duplicate UTF-8 buffers before returning strings

## Claim

`DotBoxdJsonExporter.Export` and `PluginPackageJsonSerializer.Export` serialize through a `MemoryStream`, call `ToArray()`, and then decode that copied UTF-8 buffer into the returned string, so large module/package export pays an avoidable full-payload byte-array allocation in addition to the final string.

## Evidence

- `src/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonExporter.cs:12` creates a fresh `MemoryStream` for every module export.
- `src/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonExporter.cs:13` writes UTF-8 JSON into that stream, then `src/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonExporter.cs:16` calls `stream.ToArray()` before `Encoding.UTF8.GetString(...)` creates the returned string.
- `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:13` repeats the same `MemoryStream` pattern for plugin packages, and `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:17` performs the same `ToArray()` plus string decode.
- `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:52` embeds module export through `DotBoxdJsonExporter.Write`, so package export pays this on the package-sized payload.
- Existing tests cover round-trip correctness (`tests/DotBoxd.Kernels.Tests/Misc07/DotBoxdJsonExporterTests.cs:34`, `tests/DotBoxd.Kernels.Tests/PluginAnalyzer/Core/PluginAnalyzerGeneratedPackageJsonTests.cs:34`), and the benchmark project has `JsonImportBenchmarks` but no JSON export/package export allocation benchmark.

## Impact

Generated IR and generated plugin packages can be large. Export currently needs the final `string`, but the intermediate copied byte array is extra transient allocation proportional to the entire JSON output. Repeated package export during plugin generation, diagnostics, or tooling can create avoidable Gen0/LOH pressure.

## Better target

Use a writer that avoids the extra full `ToArray()` copy, such as `ArrayBufferWriter<byte>` with `WrittenSpan`, pooled buffer ownership, or a direct string-building/export path where appropriate. The target is one final string allocation plus bounded writer overhead, not an additional full JSON byte-array copy.

## Benchmark/allocation test idea

Add BenchmarkDotNet cases for `DotBoxdJsonExporter.Export` and `PluginPackageJsonSerializer.Export` with 100, 1,000, and 10,000 statements plus metadata/live settings. Measure allocated bytes and assert export does not allocate an extra byte array proportional to the full output size beyond the returned string.
