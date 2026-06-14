---
id: PAL-0037
area: perf_alloc
status: open
priority: medium
title: Compiled binding dispatch boxes synchronous ValueTasks into Tasks
dedup_key: alloc/compiled-binding-dispatch/valuetask-astask
created_at: 2026-06-12T23:27:46.2605980+00:00
created_by: codex-performance-producer
created_commit: 
updated_at: 2026-06-12T23:27:46.2605980+00:00
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

# PAL-0037: Compiled binding dispatch boxes synchronous ValueTasks into Tasks

## Claim

Compiled binding dispatch forces every binding `ValueTask<SandboxValue>` through `AsTask()` before synchronously waiting, so result-backed synchronous bindings pay an avoidable task wrapper allocation on each compiled host call.

## Evidence

- `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:3` defines `BindingInvoker` as returning `ValueTask<SandboxValue>`, which lets synchronous bindings return a result without allocating a `Task`.
- `src/DotBoxd.Kernels.Runtime/CompiledBindingDispatcher.cs:27` invokes the descriptor and immediately calls `.AsTask().GetAwaiter().GetResult()`. For a `ValueTask` completed from a direct result, `AsTask()` has to create a `Task<SandboxValue>` wrapper before the dispatcher can read the result.
- Built-in low-cost bindings commonly complete synchronously, for example `src/DotBoxd.Kernels.Runtime/Bindings/MathBindings.cs:33`, `src/DotBoxd.Kernels.Runtime/Bindings/StringBindings.cs:14`, `src/DotBoxd.Kernels.Runtime/Bindings/SafeRandomBindings.cs:33`, `src/DotBoxd.Kernels.Runtime/Bindings/SafeTimeBindings.cs:31`, and `src/DotBoxd.Kernels.Runtime/Bindings/SafeLogBindings.cs:25` all return `ValueTask.FromResult(...)` or `ValueTask.CompletedTask`-style results.
- The interpreted dispatcher does not force this conversion: `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:186` awaits `descriptor.Invoke(...)` directly, preserving the `ValueTask` fast path.
- Existing `PAL-0013` covers compiled binding argument-array allocation, `ALG-0010` covers per-call argument revalidation, and `PAL-0036` covers cancellation token linking. This finding is separate: even after those are fixed, compiled synchronous binding dispatch still allocates through `ValueTask.AsTask()`.

## Impact

Compiled mode is supposed to reduce per-call overhead for hot pure kernels, but generic binding calls to cheap synchronous bindings still allocate a `Task<SandboxValue>` for every dispatch. Modules that call small math/string/custom pure bindings inside loops can spend allocation budget and GC time on the host dispatch bridge rather than on sandbox work, especially after the materialized executable cache is warm.

## Better target

Keep the synchronous compiled delegate boundary, but preserve the `ValueTask` completed-successfully fast path. For example, store the returned `ValueTask<SandboxValue>`, read `Result` when `IsCompletedSuccessfully`, and only fall back to `AsTask().GetAwaiter().GetResult()` for genuinely asynchronous completions. If compiled dispatch should support async host bindings more broadly, split the compiled runtime bridge into explicit sync and async paths so cheap synchronous bindings stay allocation-free.

## Benchmark/allocation test idea

Add a compiled execution benchmark with a custom pure binding returning `ValueTask.FromResult(SandboxValue.FromInt32(...))`, then execute entrypoints with 1, 100, and 10,000 binding calls after the executable cache is warm. Measure allocated bytes per run and assert the steady-state dispatch path does not allocate one `Task<SandboxValue>` per synchronous binding call.
