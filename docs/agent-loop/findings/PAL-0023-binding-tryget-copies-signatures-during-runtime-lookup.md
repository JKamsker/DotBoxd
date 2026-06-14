---
id: PAL-0023
area: perf_alloc
status: open
priority: medium
title: Binding TryGet copies signatures during runtime lookup
dedup_key: alloc/binding-registry/runtime-tryget-signature-copy
created_at: 2026-06-12T22:21:33.4513092+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:21:33.4513092+00:00
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

# PAL-0023: Binding TryGet copies signatures during runtime lookup

## Claim

Runtime binding existence and metadata checks call `BindingRegistry.TryGet`, which materializes a fresh public `BindingSignature` and parameter array copy even when callers only need to know whether a binding exists or whether it has side effects.

## Evidence

- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:127` checks `_context.Bindings.TryGet(call.Name, out _)` to decide whether a call name is a binding.
- `src/DotBoxd.Kernels.Interpreter/ExpressionEvaluator.cs:168` then fetches the actual descriptor separately with `GetDescriptor` when invoking the binding, so the `TryGet` signature object was not used for dispatch.
- `src/DotBoxd.Kernels/Model/ShortCircuitExpressionOrder.cs:59` also calls `bindings.TryGet(call.Name, out var binding)` while choosing evaluation order for short-circuit expressions, which can run during interpreted expression evaluation.
- `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:146` through `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:156` implements `BindingRegistry.TryGet` by assigning `descriptor.Signature` on success.
- `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:67` through `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:68` constructs a new `BindingSignature` for every `descriptor.Signature` access.
- `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:70` through `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:84` copies the descriptor parameter list into a new array for that signature.
- `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:132` exposes `GetDescriptor`, which already performs a direct dictionary lookup without materializing a public signature.
- This is distinct from `PAL-0013` compiled argument-array allocation and from verifier member-signature findings. The allocation here is a runtime registry abstraction leak: successful `TryGet` creates a signature copy even for existence checks.

## Impact

Interpreted code with many binding calls allocates a `BindingSignature` and parameter array copy before each actual binding dispatch. Short-circuit ordering can also allocate signatures while evaluating boolean expressions that contain binding calls. For small pure bindings, this metadata allocation can become comparable to the binding work itself and adds avoidable Gen0 pressure in interpreter-heavy hosts.

## Better target

Add a descriptor-oriented or existence-only lookup for runtime use, such as `TryGetDescriptor`, `Contains`, or an internal `TryGetRuntimeBinding`, and keep public `BindingSignature` snapshots for catalog/export APIs. Short-circuit ordering only needs stable metadata such as effects; it should not require a fresh signature copy per evaluation.

## Benchmark idea

Add an interpreter benchmark that calls a zero- or one-argument pure binding 10,000 and 100,000 times, with bindings that have 0, 1, and many parameters. Measure allocated bytes before and after replacing runtime `TryGet` signature materialization with descriptor/existence lookup.
