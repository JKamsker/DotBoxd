---
id: ALG-0001
area: perf_algorithm
status: verified
priority: high
title: JSON source map import repeatedly searches raw text
dedup_key: algorithm/json-import/source-map/repeated-rawtext-search
created_at: 2026-06-12T20:36:49.3206264+00:00
created_by: perf-reviewer
created_commit: 
updated_at: 2026-06-12T21:21:03.6278384+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:12:42.9080187+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:18:33.5804390+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T21:21:03.6278384+00:00
verified_commit: 
duplicate_of: 
---

# ALG-0001: JSON source map import repeatedly searches raw text

## Claim

`SafeIrJsonImporter.Import` builds source spans with repeated raw-text extraction and string searches, making import cost grow poorly for large JSON IR documents.

## Evidence

- `src/SafeIR.Serialization.Json/SafeIrJsonImporter.cs:12` first scans the full JSON with `JsonImportBudgetGuard.Validate`, then `JsonDocument.Parse` parses it again at `src/SafeIR.Serialization.Json/SafeIrJsonImporter.cs:13`.
- `src/SafeIR.Serialization.Json/SafeIrJsonImporter.cs:20` always constructs a `JsonSourceMap` for the whole document before reading the module.
- `src/SafeIR.Serialization.Json/Internal/JsonSourceMap.cs:38` calls `JsonElement.GetRawText()` for every visited element, allocating a raw JSON string per node.
- `src/SafeIR.Serialization.Json/Internal/JsonSourceMap.cs:39` searches the full JSON with `IndexOf(rawText, cursor, Ordinal)` per element, falling back to another full search at `src/SafeIR.Serialization.Json/Internal/JsonSourceMap.cs:42`.
- `src/SafeIR.Serialization.Json/Internal/JsonSourceMap.cs:81` computes line/column by scanning from the beginning of `_json` to the found index for each span.
- `src/SafeIR.Serialization.Json/Internal/JsonSourceMap.cs:24` repeats `GetRawText()` for every `SpanFor` lookup while importing statements and expressions.

## Impact

Current behavior is at least repeated full-document scanning and can approach quadratic work as node count and byte offsets grow. The realistic trigger is generated IR with thousands of statements/expressions near the 100,000-token import budget, where import/prepare latency matters and diagnostics still require source spans.

## Better target

Track byte offsets/line starts during a single UTF-8 reader pass, or maintain a line-start index and map parser token positions to spans without per-node `GetRawText()` plus `IndexOf`. Target should be roughly O(bytes + nodes), with bounded per-node allocation.

## Benchmark idea

Add a BenchmarkDotNet import benchmark that generates modules with 100, 1,000, and 10,000 simple statements/expressions and measures `SafeIrJsonImporter.Import` time plus allocated bytes. Include a duplicate-looking literal-heavy case to expose `_spansByRawText` queue behavior.
