---
id: COR-0049
area: correctness
status: verified
priority: high
title: Runtime value boundaries accept malformed scalar records
dedup_key: correctness/runtime-value-boundaries/malformed-scalar-records
created_at: 2026-06-12T23:24:10.9645141+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T23:41:38.8507428+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:30:15.0302821+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:34:02.1028849+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T23:41:38.8507428+00:00
verified_commit: 
duplicate_of: 
---

# COR-0049: Runtime value boundaries accept malformed scalar records

## Claim

Runtime value-boundary validation accepts public `SandboxValue` record instances whose static sandbox type is correct but whose scalar payload violates the invariants enforced by the safe factories and literal validator. A host can pass malformed `F64Value`, `SandboxPathValue`, or `SandboxUriValue` inputs into an entrypoint, and a binding can return the same malformed scalar values, without the boundary rejecting them.

## Evidence

`src/DotBoxd.Kernels/Sandbox/SandboxValue.cs` exposes public records such as `F64Value`, `SandboxPathValue`, `SandboxPath`, `SandboxUriValue`, and `SandboxUri`. The factory methods reject non-finite doubles, non-portable paths, and URIs with invalid shape, but callers can bypass those factories by constructing the records directly.

`src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs` is the shared runtime type validator used by `EntrypointBinder.RequireType(...)` and compiled binding argument validation. It verifies known value kind, declared `Type`, known/allowed expected type, collection shape, and opaque ID syntax, but it does not reject `F64Value(double.NaN)`, `F64Value(double.PositiveInfinity)`, `new SandboxPathValue(new SandboxPath("../secret.txt"))`, or `new SandboxUriValue(new SandboxUri("https://user:pass@example.com/config"))`.

`src/DotBoxd.Kernels/Model/EntrypointBinder.cs` validates entrypoint inputs through `SandboxValueValidator.RequireType(...)`. Both execution runners charge input shape before binding arguments, but `src/DotBoxd.Kernels/Sandbox/Values/SandboxValueShapeMeter.cs` treats `F64Value` as already valid and only measures path/URI text length. A single-parameter entrypoint can therefore accept and return a malformed scalar input successfully if the declared type matches.

`src/DotBoxd.Kernels/Sandbox/SandboxContext.cs` validates binding returns through `SandboxValidatedValueShapeMeter.Measure(...)`. That meter has the same gap: it checks known kind/type and opaque IDs, measures path/URI text, and ignores F64 finiteness, portable path syntax, and sandbox URI syntax. `src/DotBoxd.Kernels.Runtime/CompiledBindingDispatcher.cs` also validates binding arguments with `SandboxValueValidator.RequireType(...)`, so compiled and interpreted paths share the same malformed-scalar acceptance.

This is distinct from the existing collection-literal validation finding. `src/DotBoxd.Kernels.Validation/Internal/LiteralValueSafety.cs` now walks literal values and applies scalar literal checks, but runtime entrypoint inputs and binding returns do not reuse that scalar-safety validation.

## Impact

DotBoxd.Kernels's public value model advertises safe constructors for finite F64 values, portable relative paths, and sandbox URIs, but runtime boundaries can publish or consume values that violate those invariants. A malformed F64 can appear in successful execution results; malformed path/URI values can flow into module logic or host bindings as trusted typed values; and a buggy or adversarial binding can return invalid typed scalars while still receiving success/audit handling instead of `BindingFailure`.

## Suggested tests

Add host-boundary tests that prepare an identity entrypoint for `F64`, `SandboxPath`, and `SandboxUri`, execute it in interpreted and compiled modes with direct record-constructed invalid inputs, and assert execution fails with a sandbox error instead of succeeding with the malformed value.

Extend `HostValueBoundaryTests` with bindings whose declared return type is `F64`, `SandboxPath`, or `SandboxUri` but whose implementation returns direct record-constructed malformed values. Both interpreted and compiled execution should reject the binding return as `BindingFailure`.

## Expected behavior

Every runtime value boundary should enforce the same scalar invariants as `SandboxValue` factory methods and literal validation: F64 values must be finite, paths must be portable relative paths, and sandbox URIs must be absolute without user info. Centralize this in the validator or the validated shape meter so entrypoint input, binding arguments/returns, collection values, and compiled/interpreted paths agree.

## Deduplication key

`correctness/runtime-value-boundaries/malformed-scalar-records`
