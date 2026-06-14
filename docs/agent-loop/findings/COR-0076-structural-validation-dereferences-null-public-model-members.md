---
id: COR-0076
area: correctness
status: open
priority: medium
title: Structural validation dereferences null public model members
dedup_key: correctness/validation/structural-null-public-model-members
created_at: 2026-06-13T07:09:12.7252806+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T07:09:12.7252806+00:00
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

# COR-0076: Structural validation dereferences null public model members

## Claim

`ModuleValidator.Validate(...)` and `SandboxHost.PrepareAsync(...)` can throw implementation exceptions instead of producing validation diagnostics when programmatic IR contains null public model members for required version or type fields.

## Evidence

`src/DotBoxd.Kernels.Validation/StructuralValidator.cs` performs structural validation before the guarded function-analysis block in `ModuleValidator`. It calls `SandboxLanguage.Supports(module.TargetSandboxVersion)` and later `CheckType(function.ReturnType)` / `CheckType(parameter.Type)` without first rejecting null required members.

`src/DotBoxd.Kernels/Sandbox/SandboxLanguage.cs` dereferences `target.Major` in `Supports(...)`, so `new SandboxModule(..., targetSandboxVersion: null!, ...)` throws before the validator can return `ModuleValidationResult.Failure(...)` or `SandboxHost.PrepareAsync(...)` can raise a controlled `SandboxValidationException`.

`src/DotBoxd.Kernels.Validation/StructuralValidator.cs` also dereferences `type.Name` and `type.Arguments` through `CheckType(...)`. A programmatic module with `ReturnType = null!`, `Parameter.Type = null!`, or a `SandboxType` whose `Name` is null can therefore fail with `NullReferenceException`/implementation exceptions during structural validation instead of a diagnostic such as `E-TYPE-UNKNOWN` or a dedicated null-member diagnostic.

Existing findings cover literal-analysis exceptions and null scalar payloads, but this is a separate structural-model boundary: required module version/type metadata can crash validation before literal analysis or runtime value validation is reached.

## Impact

DotBoxd.Kernels exposes a public object model for programmatic IR construction. Tools, plugin generators, tests, or host integrations that validate generated modules can lose the fail-closed validation contract and instead surface host exceptions for malformed input. That makes package/module review behavior inconsistent with JSON import validation and can turn malformed public model data into a denial-of-service path for callers expecting diagnostics.

## Suggested test

Add direct `ModuleValidator.Validate(...)` and `SandboxHost.PrepareAsync(...)` coverage for:

- `SandboxModule.TargetSandboxVersion = null!`
- `SandboxFunction.ReturnType = null!`
- `Parameter.Type = null!`
- `new SandboxType(null!, [])` used as a return or parameter type

Each case should fail through validation diagnostics or a controlled `SandboxValidationException`, not `NullReferenceException`, `ArgumentNullException`, or another implementation exception.

## Fix direction

Make required public model members fail closed at construction/init time, or make structural validation explicitly null-safe before dereferencing version/type members. Keep direct `ModuleValidator.Validate(...)` result behavior consistent with other malformed modules, and keep `SandboxHost.PrepareAsync(...)` converting invalid module shape into controlled validation failure.
