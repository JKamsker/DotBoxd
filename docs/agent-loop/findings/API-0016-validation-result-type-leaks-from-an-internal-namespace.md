---
id: API-0016
area: api_coherence
status: fixed_pending_verification
priority: medium
title: Validation result type leaks from an Internal namespace
dedup_key: api/validation/module-validation-result/internal-namespace-leak
created_at: 2026-06-12T22:29:17.5418844+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T05:57:54.5530223+00:00
claimed_by: worker
claimed_at: 2026-06-13T05:55:19.5030479+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T05:57:54.5530223+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0016: Validation result type leaks from an Internal namespace

## Claim

The `DotBoxd.Kernels.Validation` package exposes `ModuleValidator` as a public validator, but its public `Validate(...)` method returns `ModuleValidationResult` from `DotBoxd.Kernels.Validation.Internal`. A consumer using the validation package has to depend on an `Internal` namespace for the result type of a documented package-level feature.

## Why this matters

Validation is listed as a current package surface for structural, type, effect, policy, and binding validation. If the primary validation result type lives under `.Internal`, consumers cannot tell whether `ModuleValidationResult` is stable API or implementation detail. It also makes public API docs and future API-compat baselines ambiguous because an `Internal` namespace type is part of the callable public signature.

## Evidence

- `README.md:10` lists `DotBoxd.Kernels.Validation` as the package for structural, type, effect, policy, and binding validation.
- `src/DotBoxd.Kernels.Validation/ModuleValidator.cs:5` declares public `ModuleValidator` in namespace `DotBoxd.Kernels.Validation`.
- `src/DotBoxd.Kernels.Validation/ModuleValidator.cs:7` exposes `public ModuleValidationResult Validate(SandboxModule module, IBindingCatalog bindings, SandboxPolicy? policy = null)`.
- `src/DotBoxd.Kernels.Validation/Internal/ModuleValidationResult.cs:1` declares the result namespace as `DotBoxd.Kernels.Validation.Internal`.
- `src/DotBoxd.Kernels.Validation/Internal/ModuleValidationResult.cs:5` declares `public sealed record ModuleValidationResult(...)`, so the type is externally visible even though the namespace signals implementation detail.
- `src/DotBoxd.Kernels.Validation/Internal/GlobalUsings.cs:1` adds `global using DotBoxd.Kernels.Validation.Internal` inside the project, hiding the namespace mismatch from in-assembly code but not from normal consumers.
- Existing API-0001 and API-0002 cover JSON and HTTP extension methods exposed only from internal namespaces. This finding is distinct: the validation package's public method signature leaks an internal result type.

## Suggested acceptance test

Add a consumer-facing compile/API test that references `DotBoxd.Kernels.Validation` and verifies this snippet compiles without importing `DotBoxd.Kernels.Validation.Internal`:

```csharp
using DotBoxd.Kernels;
using DotBoxd.Kernels.Validation;

var validator = new ModuleValidator();
ModuleValidationResult result = validator.Validate(module, bindings, policy);
```

If the intended API avoids naming the concrete result type, add an alternate test that proves the public return type is in a stable public namespace and is documented as such.

## Suggested fix direction

Move or forward `ModuleValidationResult` into the public `DotBoxd.Kernels.Validation` namespace, and keep a compatibility shim only if source compatibility with current internal-namespace consumers is required. Update public API docs to describe the result fields, especially `Functions`, `ModuleEffects`, `RequiredCapabilities`, and `BindingReferences`.

## Scope boundaries

Do not change validation behavior or diagnostics in this finding. The fix should clarify the public package contract for the existing validation result surface.

## Deduplication key

`api/validation/module-validation-result/internal-namespace-leak`

## Verification checklist

- [ ] Consumers can use `ModuleValidator.Validate(...)` and name the result type from a public namespace.
- [ ] No consumer-facing sample requires `DotBoxd.Kernels.Validation.Internal`.
- [ ] Public API docs describe the validation result shape.
- [ ] Existing validation behavior and diagnostics remain unchanged.
