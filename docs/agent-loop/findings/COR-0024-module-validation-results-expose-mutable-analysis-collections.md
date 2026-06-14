---
id: COR-0024
area: correctness
status: fixed_pending_verification
priority: medium
title: Module validation results expose mutable analysis collections
dedup_key: correctness/validation/module-result/mutable-analysis-collections
created_at: 2026-06-12T22:19:03.7448666+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T05:54:20.7072928+00:00
claimed_by: worker
claimed_at: 2026-06-13T05:51:07.6803710+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T05:54:20.7072928+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0024: Module validation results expose mutable analysis collections

## Claim

`ModuleValidationResult` exposes mutable validator-owned analysis collections as `IReadOnly*` properties without snapshotting them. Callers can observe or mutate diagnostics, function analysis, required capabilities, and binding-reference sets after validation, so the public validation result is not an immutable record of the validation boundary.

## Evidence

`src/DotBoxd.Kernels.Validation/ModuleValidator.cs:9` creates a mutable `List<SandboxDiagnostic>` and passes that same list to `ModuleValidationResult.Failure` at `src/DotBoxd.Kernels.Validation/ModuleValidator.cs:12`. On the success path, `src/DotBoxd.Kernels.Validation/ModuleValidator.cs:16` through `src/DotBoxd.Kernels.Validation/ModuleValidator.cs:22` passes the mutable `functions` dictionary, `requiredCapabilities` hash set, and `bindingReferences` dictionary directly into the result.

`src/DotBoxd.Kernels.Validation/Internal/ModuleValidationResult.cs:5` through `src/DotBoxd.Kernels.Validation/Internal/ModuleValidationResult.cs:11` declares the public result as a plain record with `IReadOnlyList`, `IReadOnlyDictionary`, and `IReadOnlySet` properties, but it has no constructor body or init accessors that copy those inputs. `ModuleValidationResult.Failure` at `src/DotBoxd.Kernels.Validation/Internal/ModuleValidationResult.cs:13` also returns the caller-provided diagnostics list directly.

Existing public immutability coverage in `tests/DotBoxd.Kernels.Tests/Misc06/PublicModelImmutabilityTests.cs` covers modules, statement/expression nodes, plugin manifests, sandbox values, validation exceptions, audit events, sinks, and execution results, but it does not cover `ModuleValidationResult` or its nested analysis collections.

A minimal repro shape is:

```csharp
var diagnostics = new List<SandboxDiagnostic> { new("E-ONE", "first") };
var result = ModuleValidationResult.Failure(diagnostics);
diagnostics.Add(new SandboxDiagnostic("E-TWO", "second"));
Assert.Single(result.Diagnostics); // currently fails: result aliases diagnostics
```

For successful validation results, a caller that casts `result.Diagnostics` or `result.RequiredCapabilities` back to the concrete `List<T>`/`HashSet<T>` can mutate the supposedly read-only result observed by later policy tooling.

## Risk

Validation results are trust-boundary evidence used by import, prepare, policy review, and plugin-package validation paths. If the result aliases mutable working collections, diagnostics and required capability/effect evidence can change after validation has completed. That can make audit logs, policy decisions, or error reporting disagree about what was validated, and it breaks the public model immutability guarantee already enforced for nearby public models.

## Suggested test

Extend `PublicModelImmutabilityTests` with coverage for `ModuleValidationResult.Failure`: mutate the original diagnostics list after construction and assert the result stays unchanged. Add a success-path test that validates a small module, attempts to mutate any concrete `List<T>`, `Dictionary<TKey,TValue>`, or `HashSet<T>` exposed through `Diagnostics`, `Functions`, `RequiredCapabilities`, and `BindingReferences`, and asserts either the cast is impossible or the result snapshot remains unchanged.

## Expected behavior

`ModuleValidationResult` should snapshot all collection inputs at construction and expose read-only collection instances that cannot be mutated by callers or by later changes to validator-owned working collections.

## Suggested fix direction

Give `ModuleValidationResult` the same defensive-copy pattern used by other public models: copy diagnostics with `ModelCopy.List`, copy dictionaries into read-only dictionaries, copy required capability sets into an immutable/read-only set, and copy each binding-reference set before storing it. `Failure` should also snapshot diagnostics rather than retaining the input list.

## Deduplication key

`correctness/validation/module-result/mutable-analysis-collections`
