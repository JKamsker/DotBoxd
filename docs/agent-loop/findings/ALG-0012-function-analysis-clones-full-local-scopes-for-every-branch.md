---
id: ALG-0012
area: perf_algorithm
status: open
priority: medium
title: Function analysis clones full local scopes for every branch
dedup_key: alg/function-analysis/scope-clone-per-branch
created_at: 2026-06-12T22:27:05.7537096+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:27:05.7537096+00:00
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

# ALG-0012: Function analysis clones full local scopes for every branch

## Claim

Function analysis clones the entire local-variable scope for every branch and loop body. The clone copies all locals into a new dictionary, so validation cost grows with `branch-or-loop-count * in-scope-local-count` even when a block only reads a small subset of locals.

## Evidence

- `src/DotBoxd.Kernels.Validation/FunctionAnalyzer.cs:108` analyzes `if` statements by passing `scope.Clone()` for the `then` block.
- `src/DotBoxd.Kernels.Validation/FunctionAnalyzer.cs:113` does the same for the `else` block, so one conditional clones the full scope twice.
- `src/DotBoxd.Kernels.Validation/FunctionAnalyzer.cs:121` clones the full scope for every `while` body.
- `src/DotBoxd.Kernels.Validation/FunctionAnalyzer.cs:132` clones the full scope for every `for` body before adding the loop local.
- `src/DotBoxd.Kernels.Validation/FunctionScope.cs:23` implements `Clone()` as `new Dictionary<string, SandboxType>(_locals, StringComparer.Ordinal)`, copying every visible local into a fresh dictionary.
- This is separate from ALG-0005 binding-reference graph walks and ALG-0007 plugin package validation entrypoint scans; it is the core module validator's lexical scope representation doing full dictionary copies per control-flow construct.

## Impact

Large generated or plugin-produced DotBoxd.Kernels functions often accumulate locals before branching. Nested conditionals/loops then allocate and copy the same local map repeatedly during `ModuleValidator.Validate`, increasing validation latency and allocation pressure before execution can start.

## Better target

Use a persistent/parent-linked scope or copy-on-write overlay so child blocks only store new or changed locals and fall back to the parent for lookups. This keeps block analysis direct while reducing clone cost from O(all visible locals) to O(locals introduced or changed in that block).

## Benchmark/allocation test idea

Add a validation benchmark for functions with 100, 1,000, and 10,000 locals followed by nested `if`/`while`/`for` blocks. Measure allocations in `ModuleValidator.Validate` and assert control-flow scope creation does not copy the full local dictionary per block.
