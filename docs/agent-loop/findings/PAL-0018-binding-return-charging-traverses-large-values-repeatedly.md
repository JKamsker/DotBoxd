---
id: PAL-0018
area: perf_alloc
status: verified
priority: medium
title: Binding return charging traverses large values repeatedly
dedup_key: alloc/binding-return/quota/type-shape-triple-traversal
created_at: 2026-06-12T22:11:14.0930789+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:47:26.6053957+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:20:18.1498541+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T22:38:26.9289973+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T22:47:26.6053957+00:00
verified_commit: 
duplicate_of: 
---

# PAL-0018: Binding return charging traverses large values repeatedly

## Claim

Binding return charging traverses returned sandbox values multiple times: once for type validation, once to measure string bytes for per-byte fuel, and again to charge the value shape when no return credit exists.

## Evidence

- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:220` begins `ChargeBindingReturn` for every host binding result.
- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:222` calls `SandboxValueValidator.RequireType`, whose traversal allocates a `HashSet<object>` and `Stack<Frame>` at `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:14` and `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:15`.
- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:228` then calls `BindingReturnCost.MeasureBytes`; `src/DotBoxd.Kernels/Bindings/BindingReturnCost.cs:6` delegates to `SandboxValueShapeMeter.Measure(value).StringBytes`.
- `src/DotBoxd.Kernels/Sandbox/Values/SandboxValueShapeMeter.cs:11` and `src/DotBoxd.Kernels/Sandbox/Values/SandboxValueShapeMeter.cs:12` allocate a second traversal `HashSet<object>` and `Stack<Frame>` for shape measurement.
- If the value was not already credited, `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:231` calls `ChargeValue(value)`, which runs `ResourceMeter.ChargeValue` and another `SandboxValueShapeMeter.Measure` traversal.
- This is distinct from ALG-0002 collection mutation copying and PAL-0003 map traversal buffering: the returned value may already exist, but quota/accounting walks it repeatedly at the binding boundary.

## Impact

A binding returning a large list/map or nested structure can pay two to three full graph traversals and two shape-meter allocation sets before returning to sandbox code. That increases host binding latency and Gen0 pressure exactly on collection/runtime quota paths, and it scales with returned payload size even when the binding itself is cheap or in-memory.

## Better target

Combine type validation, cycle detection, shape measurement, and string-byte accounting into one binding-return traversal, or return a reusable validation/shape result from `SandboxValueValidator`. `ChargeBindingReturn` should compute the needed shape once and use it for both per-byte fuel and quota charging.

## Benchmark/allocation test idea

Add a BenchmarkDotNet binding-return benchmark with a pure in-memory binding returning flat and nested lists/maps at 100, 1,000, and 10,000 elements, both with and without string values and return credits. Measure allocations inside `SandboxContext.ChargeBindingReturn` and assert large returns are traversed once for validation/shape accounting.
