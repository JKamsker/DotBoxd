---
id: PAL-0003
area: perf_alloc
status: verified
priority: medium
title: Map validation and metering buffer reversed entries
dedup_key: alloc/sandbox/map-validation/reverse-buffer
created_at: 2026-06-12T21:00:42.8012382+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T21:34:59.5852185+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:31:19.6730521+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:32:12.7930647+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T21:34:59.5852185+00:00
verified_commit: HEAD
duplicate_of: 
---

# PAL-0003: Map validation and metering buffer reversed entries

## Claim

Sandbox map validation and shape metering allocate a reversed buffer for every map they traverse because they use LINQ `Reverse()` on dictionary values in hot recursive walks.

## Evidence

- `src/SafeIR.Core/Sandbox/Values/SandboxValueShapeMeter.cs:16` walks arbitrary nested `SandboxValue` graphs to measure resource shape.
- `src/SafeIR.Core/Sandbox/Values/SandboxValueShapeMeter.cs:96` iterates `foreach (var pair in map.Values.Reverse())` before pushing key/value frames.
- `src/SafeIR.Core/Sandbox/SandboxValueValidator.cs:17` separately walks arbitrary nested values for type validation.
- `src/SafeIR.Core/Sandbox/SandboxValueValidator.cs:88` also iterates `foreach (var pair in map.Values.Reverse())` before pushing key/value frames.
- LINQ `Enumerable.Reverse()` buffers the source sequence before yielding in reverse order, so each visited map creates temporary storage proportional to the number of entries.
- The test suite has quota and behavior tests around collections, but the benchmark project only contains IPC benchmarks and no allocation test for validating or metering large nested map inputs.

## Impact

Large map inputs pay an extra O(map-size) temporary allocation in both validation and metering, in addition to the existing traversal stack. Because these routines run on sandbox boundary/resource-accounting paths, hosts processing large map payloads can see avoidable Gen0 pressure and doubled traversal memory for map-heavy inputs.

## Better target

Avoid LINQ `Reverse()` in these hot walks. If stable reverse traversal is required, use a reusable/indexable representation for map entries or push frames in forward order and accept reversed processing when semantics permit. The target is one traversal with no per-map full-entry buffer beyond the explicit work stack.

## Benchmark idea

Add an allocation test or BenchmarkDotNet case that builds flat and nested `MapValue` inputs with 100, 1,000, and 10,000 entries, then measures allocated bytes for `SandboxValueValidator.RequireType` and `SandboxValueShapeMeter.Measure`. Include a regression assertion that map traversal does not allocate O(entry-count) temporary buffers.
