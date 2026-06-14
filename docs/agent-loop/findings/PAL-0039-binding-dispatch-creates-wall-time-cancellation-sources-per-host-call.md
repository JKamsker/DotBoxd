---
id: PAL-0039
area: perf_alloc
status: open
priority: medium
title: Binding dispatch creates wall-time cancellation sources per host call
dedup_key: alloc/binding-dispatch/wall-time-linked-cts-per-call
created_at: 2026-06-13T06:24:33.8244006+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:24:33.8244006+00:00
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

# PAL-0039: Binding dispatch creates wall-time cancellation sources per host call

## Claim

Both interpreted and compiled binding dispatch create a linked wall-time `CancellationTokenSource` for every host binding call, even when the run cancellation token is not cancelable and the binding completes synchronously.

## Evidence

- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:245` through `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:249` implements `CreateWallTimeToken()` by always calling `CancellationTokenSource.CreateLinkedTokenSource(CancellationToken)` and then `CancelAfter(Budget.RemainingWallTime())`.
- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:181` through `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:186` calls `CreateWallTimeToken()` for every interpreted binding invocation before awaiting `descriptor.Invoke(...)`.
- `src/DotBoxd.Kernels.Runtime/CompiledBindingDispatcher.cs:22` through `src/DotBoxd.Kernels.Runtime/CompiledBindingDispatcher.cs:27` does the same for every compiled binding invocation before synchronously reading the binding result.
- Many built-in bindings are cheap and usually complete synchronously, so this timer/linked-token allocation sits on the fast path before binding work actually starts.
- Existing `PAL-0036` covers plugin kernel entrypoint cancellation linking, `PAL-0037` covers compiled `ValueTask.AsTask()` boxing, and `PAL-0013` covers compiled argument arrays. This finding is the independent per-binding wall-time CTS/timer allocation shared by interpreted and compiled binding dispatch.

## Impact

A sandbox loop that calls a cheap pure/custom binding thousands of times allocates a linked CTS, cancellation registration, and timer state for each call. That is avoidable work when the host run token is default/non-cancelable and the wall-time deadline is already tracked by `ResourceMeter`. It also compounds the compiled binding dispatch costs already tracked in other findings.

## Suggested fix

Represent the run wall-clock deadline once on `SandboxContext` or `ResourceMeter`, and avoid creating a new token source for bindings that do not need asynchronous timeout cancellation. For the async case, lazily create a linked timeout source only when both cancellation and timeout signaling are required, or use a shared per-run deadline token. Preserve timeout semantics by checking the deadline before and after synchronous completions and by passing an actual timeout token only to bindings that may await external work.

## Benchmark/allocation test idea

Add interpreted and compiled benchmarks with a custom synchronous binding returning `ValueTask.FromResult(SandboxValue.Unit)`, executed 1, 1,000, and 100,000 times with the default cancellation token and a generous wall-time budget. Measure allocations per binding call and assert the fast path does not allocate a linked `CancellationTokenSource` or timer per invocation.
