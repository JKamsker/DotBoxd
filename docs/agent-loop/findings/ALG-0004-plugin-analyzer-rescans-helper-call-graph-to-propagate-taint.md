---
id: ALG-0004
area: perf_algorithm
status: verified
priority: medium
title: Plugin analyzer rescans helper call graph to propagate taint
dedup_key: algorithm/plugin-analyzer/helper-callgraph/repeated-full-scan
created_at: 2026-06-12T21:00:45.7132641+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T21:44:56.3471060+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:39:15.2533115+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:42:30.2605486+00:00
fixed_commit: 
verified_by: independent-verifier
verified_at: 2026-06-12T21:44:56.3471060+00:00
verified_commit: 
duplicate_of: 
---

# ALG-0004: Plugin analyzer rescans helper call graph to propagate taint

## Claim

The plugin analyzer propagates forbidden-helper taint by repeatedly scanning the entire helper call bag until no changes occur, which can become quadratic on large helper graphs.

## Evidence

- `src/DotBoxd.Plugins.Analyzer/Analysis/DotBoxdPluginAnalyzer.cs:181` stores all helper calls in a `ConcurrentBag<HelperCall>` as operation analysis records invocations.
- `src/DotBoxd.Plugins.Analyzer/Analysis/DotBoxdPluginAnalyzer.cs:207` calls `PropagateForbiddenHelpers()` during compilation-end reporting.
- `src/DotBoxd.Plugins.Analyzer/Analysis/DotBoxdPluginAnalyzer.cs:224` loops `while (changed)`.
- `src/DotBoxd.Plugins.Analyzer/Analysis/DotBoxdPluginAnalyzer.cs:226` scans every recorded helper call on every propagation pass.
- `src/DotBoxd.Plugins.Analyzer/Analysis/DotBoxdPluginAnalyzer.cs:227` only taints a caller when the callee is already tainted, so a long chain discovered in an unfavorable bag order can require one full scan per chain level.
- Analyzer tests cover diagnostics, but the benchmark project has no Roslyn analyzer performance benchmark for helper graph size, call-chain depth, or fan-out.

## Impact

Plugin projects with many helper methods can force compilation-end analyzer work toward O(V * E) instead of O(V + E). Since analyzers run inside build/IDE compilation, this affects developer feedback latency and CI build time for large plugin packages.

## Better target

Build reverse adjacency from callee to callers and run a queue/BFS seeded by initially forbidden methods. That processes each helper edge once, avoids repeated full-bag scans, and preserves the current diagnostic semantics.

## Benchmark idea

Add an analyzer benchmark or perf test that generates plugin source with helper chains/fan-out at 100, 1,000, and 10,000 calls, then measures analyzer execution time and allocations. Include a worst-case chain where the forbidden call appears at the leaf and callers are discovered in reverse propagation order.
