---
id: PAL-0019
area: perf_alloc
status: open
priority: medium
title: Canonical module hashing materializes nested records before hashing
dedup_key: alloc/canonical-module-hash/nested-record-materialization
created_at: 2026-06-12T22:15:08.8904927+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:15:08.8904927+00:00
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

# PAL-0019: Canonical module hashing materializes nested records before hashing

## Claim

Canonical module hashing materializes a full nested canonical string graph before hashing, so large modules pay avoidable transient string/list/array allocation on prepare and plan-integrity validation.

## Evidence

- `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:7` hashes modules through `CanonicalEncoding.HashRecord(Serialize(module))`, so hashing first builds the complete canonical module text.
- `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:10` creates a `CanonicalWriter`, and `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:17` returns `writer.ToString()`, materializing the whole module serialization before SHA-256 sees any bytes.
- Expression/value helpers build nested canonical strings bottom-up: `Type` allocates `new List<string?>` at `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:182`, `Call` allocates another list at `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:193`, `ListLiteral` allocates at `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:208`, and `MapLiteral` allocates/sorts an entry string array plus a fields list at `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:220` through `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:227`.
- `CanonicalEncoding.Record` creates a new `StringBuilder` per record at `src/DotBoxd.Kernels/CanonicalEncoding.cs:13`, and `Escape` creates another `StringBuilder` plus escaped string per non-null field at `src/DotBoxd.Kernels/CanonicalEncoding.cs:46` through `src/DotBoxd.Kernels/CanonicalEncoding.cs:52`.
- `CanonicalEncoding.HashText` then allocates a UTF-8 byte array for the already-materialized canonical text at `src/DotBoxd.Kernels/CanonicalEncoding.cs:31`.
- `ExecutionPlanBuilder.Build` calls `CanonicalModuleHasher.Hash(module)` during plan creation at `src/DotBoxd.Hosting/Execution/ExecutionPlanBuilder.cs:17`, and `ExecutionPlanGuard.EnsurePrepared` rebuilds the expected plan on execution at `src/DotBoxd.Hosting/Execution/ExecutionPlanGuard.cs:39`.

## Impact

This is a prepare/execute-guard allocation issue, not a semantic problem. A generated module with thousands of expressions, nested list/map literals, or repeated type nodes creates many intermediate canonical strings and collection objects before producing the final hash. The final canonical text and UTF-8 byte array also duplicate the module representation in memory. Hosts that prepare or validate many plugin/generated modules can see avoidable Gen0 pressure and higher peak memory.

## Better target

Stream canonical records directly into an incremental hash or pooled encoder, writing escaped fields into a reusable buffer instead of returning nested strings. Keep deterministic ordering for unordered sections/maps, but avoid materializing the whole module and every nested expression/type/value record as standalone strings.

## Benchmark/allocation test idea

Add a BenchmarkDotNet benchmark for `CanonicalModuleHasher.Hash` and `SandboxHost.PrepareAsync` with generated modules containing 100, 1,000, and 10,000 statements plus nested list/map literals. Measure allocated bytes and peak record sizes, and assert hashing allocation grows with bounded writer/hash buffers rather than with every nested node string.
