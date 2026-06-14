---
id: COR-0052
area: correctness
status: fixed_pending_verification
priority: medium
title: SemVersion accepts negative public model components
dedup_key: correctness/public-model/semversion-negative-components
created_at: 2026-06-12T23:27:55.2123242+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T00:28:11.0829748+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:26:05.3478935+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:28:11.0829748+00:00
fixed_commit: pending
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0052: SemVersion accepts negative public model components

## Claim

The public `SemVersion` record can be constructed with negative version components, and validators do not reject those impossible versions. Programmatic modules can therefore pass structural validation with targets such as `new SemVersion(1, -1, 0)`, because the support check treats the value as earlier than `1.0.0`.

## Evidence

`src/DotBoxd.Kernels/Model/SemVersion.cs` declares `public sealed record SemVersion(int Major, int Minor, int Patch)` without a constructor body or range checks. The parser uses `NumberStyles.None`, so negative text is rejected, but the public constructor and record `with` path still allow negative `Major`, `Minor`, or `Patch` values.

`src/DotBoxd.Kernels/Sandbox/SandboxLanguage.cs` implements support as `target.Major == CurrentVersion.Major && target.CompareTo(CurrentVersion) <= 0`. For a programmatic value such as `new SemVersion(1, -1, 0)`, the major matches current version `1.0.0` and comparison returns less than current, so `Supports(...)` returns true.

`src/DotBoxd.Kernels.Validation/StructuralValidator.cs` uses `SandboxLanguage.Supports(module.TargetSandboxVersion)` as the target sandbox version gate and does not separately validate non-negative components. It also does not validate `module.Version` at all. `src/DotBoxd.Kernels/Bindings/BindingRegistryValidator.cs` validates binding ids, effects, cost model, compiled targets, and types, but it never validates `BindingDescriptor.Version`, so negative binding versions can enter the binding manifest hash.

## Impact

DotBoxd.Kernels can accept and hash modules or bindings with semantic versions that cannot be produced by the JSON parser and are not valid SemVer values. That creates inconsistent behavior between JSON-imported IR and programmatically constructed IR, allows unsupported target versions such as `1.-1.0` through the runtime support gate, and can publish impossible binding or module version metadata into canonical hashes and manifests.

## Suggested tests

Add tests that construct modules with `TargetSandboxVersion = new SemVersion(1, -1, 0)` and assert `ModuleValidator.Validate(...)` reports `E-IR-VERSION`. Add tests that `new SemVersion(-1, 0, 0)`, `new SemVersion(1, -1, 0)`, and `SemVersion.One with { Patch = -1 }` are rejected or normalized according to the chosen API contract. Add binding registry validation coverage for a `BindingDescriptor` with a negative `Version` component.

## Expected behavior

`SemVersion` should enforce non-negative components at construction and through `with` initializers, or every validator that accepts public model instances should reject negative version components before support checks and canonical hashing.

## Deduplication key

`correctness/public-model/semversion-negative-components`
