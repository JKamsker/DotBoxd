---
id: COR-0041
area: correctness
status: fixed_pending_verification
priority: medium
title: ModuleValidator can throw instead of returning failed literal validation results
dedup_key: correctness/validation/literal-exception-bypasses-module-validation-result
created_at: 2026-06-12T23:02:26.6014900+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-12T23:07:02.9679278+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:04:07.5604437+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:07:02.9679278+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0041: ModuleValidator can throw instead of returning failed literal validation results

## Problem

`ModuleValidator.Validate(...)` returns `ModuleValidationResult` and normally reports invalid modules by returning `Succeeded = false` with diagnostics. Invalid programmatic literal values can bypass that result path: `FunctionAnalyzer` calls `LiteralExpressionAnalyzer.Analyze`, `LiteralExpressionAnalyzer.ValidateLiteralValue` catches `SandboxRuntimeException`, and `InvalidLiteral(...)` throws a `SandboxValidationException` directly. `ModuleValidator.Validate` does not catch and translate that exception into a failed `ModuleValidationResult`.

## Impact

Direct callers of the public validator cannot reliably use `Succeeded` and `Diagnostics` as the validation boundary. A malformed programmatic literal can unwind the validator instead of returning a result, while structural errors in the same API return `ModuleValidationResult.Failure(...)`. That makes validation result handling inconsistent for hosts and tooling that validate programmatically constructed IR without going through `SandboxHost.PrepareAsync`.

## Evidence

- `src/DotBoxd.Kernels.Validation/ModuleValidator.cs` returns early with `ModuleValidationResult.Failure(...)` for structural diagnostics, but does not catch validation exceptions raised during function analysis.
- `src/DotBoxd.Kernels.Validation/FunctionAnalyzer.cs` routes `LiteralExpression` to `LiteralExpressionAnalyzer.Analyze(...)`.
- `src/DotBoxd.Kernels.Validation/Internal/LiteralExpressionAnalyzer.cs` converts invalid literal values into a thrown `SandboxValidationException` instead of appending diagnostics to the result under construction.
- `tests/DotBoxd.Kernels.Tests/Misc06/ProgrammaticIrValidationTests.cs` shows programmatic collection literals are a supported validation surface.

## Fix direction

Keep `ModuleValidator.Validate(...)` fail-closed through its result object. Either make literal analysis append diagnostics instead of throwing, or catch `SandboxValidationException` at the function-analysis/module-validation boundary and merge its diagnostics before returning a failed `ModuleValidationResult`. Add a direct `ModuleValidator.Validate(...)` regression test with an invalid programmatic literal and assert `Succeeded == false` with the expected diagnostic code.
