---
id: ALG-0010
area: perf_algorithm
status: open
priority: medium
title: Compiled binding dispatch revalidates arguments per call
dedup_key: algorithm/compiled-binding-dispatch/recursive-argument-validation-per-call
created_at: 2026-06-12T22:20:57.0500176+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:20:57.0500176+00:00
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

# ALG-0010: Compiled binding dispatch revalidates arguments per call

## Claim

Compiled binding dispatch recursively validates every binding argument on every call even though compiled IR has already been verified against the binding signature, so large collection arguments are rewalked per binding invocation before the binding runs.

## Evidence

- `src/SafeIR.Runtime/CompiledRuntime.cs:244` and `src/SafeIR.Runtime/CompiledRuntime.cs:245` route compiled binding calls through `CompiledBindingDispatcher.CallBinding`.
- `src/SafeIR.Runtime/CompiledBindingDispatcher.cs:7` through `src/SafeIR.Runtime/CompiledBindingDispatcher.cs:15` resolves the descriptor and calls `ValidateArguments` before charging or invoking the binding.
- `src/SafeIR.Runtime/CompiledBindingDispatcher.cs:66` through `src/SafeIR.Runtime/CompiledBindingDispatcher.cs:83` checks arity and then calls `SandboxValueValidator.RequireType` for every argument.
- `SandboxValueValidator.RequireType` allocates traversal state and walks nested values from `src/SafeIR.Core/Sandbox/SandboxValueValidator.cs:14` through `src/SafeIR.Core/Sandbox/SandboxValueValidator.cs:46`, pushing list children at `src/SafeIR.Core/Sandbox/SandboxValueValidator.cs:63` through `src/SafeIR.Core/Sandbox/SandboxValueValidator.cs:68` and map keys/values at `src/SafeIR.Core/Sandbox/SandboxValueValidator.cs:86` through `src/SafeIR.Core/Sandbox/SandboxValueValidator.cs:92`.
- The diagnostic text at `src/SafeIR.Runtime/CompiledBindingDispatcher.cs:81` says the argument type must match the verified plan, which indicates this is a runtime recheck after compile/verification rather than the primary type proof.
- Interpreted binding calls do not have the same dispatcher-level `ValidateArguments` step; `src/SafeIR.Interpreter/ExpressionEvaluator.cs:186` invokes the descriptor with the evaluated argument list after `ChargeBindingCall`.
- This is distinct from `PAL-0013`, which covers heap allocation of compiled binding argument arrays. Even if the array allocation is fixed, the dispatcher will still recursively revalidate each argument value on every compiled binding call.

## Impact

Compiled modules that repeatedly call bindings with list or map arguments pay a full recursive type walk for each argument on every call. A loop passing the same 10,000-element collection to a pure or host binding 1,000 times performs millions of avoidable validation steps before the binding body sees the value, reducing the benefit of compiled execution and adding per-call traversal allocations from the validator state.

## Better target

Let the compiled verifier and generated code carry the binding argument type proof, and restrict full recursive argument validation to boundary cases where untrusted host values enter the sandbox. If runtime defense-in-depth is still required, use shallow checks for scalar/list/map wrapper type and declared element metadata, or make recursive validation configurable for debug/strict builds rather than unconditional on the compiled hot path.

## Benchmark idea

Add a compiled-mode benchmark that calls a no-op or pure host binding with scalar, 100-element, 1,000-element, and 10,000-element list/map arguments in a tight loop. Measure dispatcher time and allocations before and after removing or reducing recursive `ValidateArguments` work, separately from the argument-array allocation tracked by `PAL-0013`.
