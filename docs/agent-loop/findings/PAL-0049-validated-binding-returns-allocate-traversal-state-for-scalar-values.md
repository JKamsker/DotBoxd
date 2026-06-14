---
id: PAL-0049
area: perf_alloc
status: open
priority: medium
title: Validated binding returns allocate traversal state for scalar values
dedup_key: alloc/binding-return/validated-meter/scalar-traversal-state
created_at: 2026-06-13T06:50:35.9110568+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:50:35.9110568+00:00
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

# PAL-0049: Validated binding returns allocate traversal state for scalar values

## Claim

`SandboxValidatedValueShapeMeter.Measure` allocates recursive traversal state for every binding return even when the returned value is scalar and needs no graph walk. The combined binding-return meter fixed the old repeated traversal, but its scalar path still starts by allocating a `HashSet<object>` and `Stack<Frame>` before it can reach unit/bool/numeric or text-only cases.

## Evidence

- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs` routes every host binding result through `ChargeBindingReturn(...)`, which now calls `SandboxValidatedValueShapeMeter.Measure(...)` before deciding whether a return credit can skip quota charging.
- `src/DotBoxd.Kernels/Sandbox/Values/SandboxValidatedValueShapeMeter.cs` creates a reference-tracking `HashSet<object>` and traversal `Stack<Frame>` at the start of `Measure(...)`, before switching on the value kind.
- The same method handles `UnitValue`, `BoolValue`, `I32Value`, `I64Value`, and `F64Value` with no recursive work after those allocations, and handles string/path/URI-like values by computing text shape without needing cycle detection.
- Existing `PAL-0018` covered repeated large-value binding-return traversals and is now verified after the combined meter change. Existing `PAL-0045` covers the unvalidated `ResourceMeter.ChargeValue` path for literals and entrypoint inputs. This finding is the remaining scalar allocation in the validated binding-return boundary.

## Impact

Cheap host bindings that return scalar values are common in plugin predicates, policy helpers, counters, and small host facades. Interpreted and compiled binding dispatch both call `ChargeBindingReturn`, so a loop or high-frequency plugin hook invoking a scalar-returning binding pays two short-lived collection allocations per binding result even though validation and shape accounting can be decided from the scalar wrapper and expected type directly.

## Better target

Add a scalar fast path to `SandboxValidatedValueShapeMeter.Measure` before allocating traversal collections. Validate known kind, expected type, and scalar invariants directly for unit/bool/numeric/text/path/URI values, compute the scalar/text `ValueShape`, and allocate the `HashSet<object>` plus `Stack<Frame>` only for list or map values that need recursive traversal and cycle detection.

## Benchmark/allocation test idea

Add an allocation benchmark for `SandboxContext.ChargeBindingReturn` using no-op bindings that return unit, bool, i32, string, and small list/map values across 1, 1,000, and 100,000 calls. Assert scalar binding returns do not allocate traversal collections while list/map returns still validate and charge correctly.
