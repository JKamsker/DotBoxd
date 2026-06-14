---
id: PAL-0045
area: perf_alloc
status: open
priority: medium
title: Scalar value charging allocates traversal state per charge
dedup_key: alloc/resource-meter/scalar-value-charge/traversal-state
created_at: 2026-06-13T06:41:13.6728032+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:41:13.6728032+00:00
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

# PAL-0045: Scalar value charging allocates traversal state per charge

## Claim

`ResourceMeter.ChargeValue` allocates traversal state even for scalar values whose resource shape is known without walking a graph. Every charged scalar literal or scalar entrypoint input currently creates a `HashSet<object>` and `Stack<Frame>` before the meter reaches the scalar fast-exit cases.

## Evidence

- `src/DotBoxd.Kernels/Model/Resources.cs:99` and `src/DotBoxd.Kernels/Model/Resources.cs:101` route every value charge through `SandboxValueShapeMeter.Measure(...)`.
- `src/DotBoxd.Kernels/Sandbox/Values/SandboxValueShapeMeter.cs:11` and `src/DotBoxd.Kernels/Sandbox/Values/SandboxValueShapeMeter.cs:12` allocate a reference-tracking `HashSet<object>` and traversal `Stack<Frame>` before looking at the value kind.
- `src/DotBoxd.Kernels/Sandbox/Values/SandboxValueShapeMeter.cs:52` and `src/DotBoxd.Kernels/Sandbox/Values/SandboxValueShapeMeter.cs:53` show `UnitValue`, `BoolValue`, `I32Value`, `I64Value`, and `F64Value` do no traversal work after those allocations have already happened.
- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:231` through `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:234` charge every interpreted literal expression through this path.
- `src/DotBoxd.Kernels.Interpreter/InterpreterEvaluator.cs:31` and `src/DotBoxd.Hosting/Execution/CompiledExecutionRunner.cs:36` charge entrypoint input before each interpreted or compiled run, so scalar inputs also pay the traversal-state allocation on every execution.
- Existing `PAL-0018` covers repeated binding-return traversal, `PAL-0003` covers map traversal buffering, and `ALG-0021` covers entrypoint charge-plus-bind double traversal. This finding is narrower: a single scalar `ChargeValue` call allocates traversal collections even though scalar shape can be computed directly.

## Impact

Scalar-heavy interpreted code, cheap scalar entrypoint calls, and small host-driven executions can spend allocation budget on metering scaffolding rather than sandbox work. A loop evaluating thousands of scalar literals or a service repeatedly invoking scalar entrypoints pays two short-lived collection allocations per charge even when the resulting `ValueShape` is zero or text-only. That adds avoidable Gen0 pressure to the runtime accounting path and makes scalar workloads look more allocation-heavy than their actual sandbox behavior.

## Better target

Add a scalar fast path before allocating traversal state. `SandboxValueShapeMeter.Measure` or `ResourceMeter.ChargeValue` can directly return/charge zero shape for unit/bool/numeric values and directly compute text shape for string/opaque/path/URI values. Allocate `HashSet<object>` and `Stack<Frame>` only when the value is a list or map that may need recursive traversal and cycle detection.

## Benchmark/allocation test idea

Add allocation benchmarks for `ResourceMeter.ChargeValue` with unit, bool, i32, string, and small list/map values across 1, 1,000, and 100,000 charges. Add an interpreted literal-loop benchmark that executes scalar literals in a loop and assert scalar value charging does not allocate traversal collections on the steady-state scalar path.
