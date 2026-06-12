---
id: API-0018
area: api_coherence
status: open
priority: medium
title: Verifier package lacks public diagnostic code reference
dedup_key: api/verifier/diagnostic-reference/missing-public-docs
created_at: 2026-06-12T23:12:05.7202659+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T23:12:05.7202659+00:00
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

# API-0018: Verifier package lacks public diagnostic code reference

## Claim

`SafeIR.Verifier` exposes verifier diagnostics as stable-looking `V-*` codes through its public result model, but the public docs do not provide a diagnostic reference that maps those codes to meaning, likely causes, severity, and remediation.

## Evidence

- `README.md:17` lists `SafeIR.Verifier` as a current package, but the README does not link to a verifier diagnostic reference or describe any `V-*` code families.
- `src/SafeIR.Verifier/Generated/VerificationModels.cs:55` defines the public `VerificationDiagnostic(string Code, string Message)` model, and `src/SafeIR.Verifier/Generated/VerificationModels.cs:59` exposes those diagnostics on `VerificationResult` returned by the public verifier interface at `src/SafeIR.Verifier/Generated/VerificationModels.cs:64`.
- The verifier emits many user-visible codes, for example `V-MANIFEST-HASH` at `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:21`, `V-PE-FORMAT` at `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:45`, `V-ASM-REF` at `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:93`, `V-TYPE-FORBIDDEN` at `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:112`, `V-OPCODE` at `src/SafeIR.Verifier/OpCodeVerifier.cs:54`, `V-MEMBER` at `src/SafeIR.Verifier/OpCodeVerifier.cs:95`, and `V-COMPILED-SHAPE` at `src/SafeIR.Verifier/Generated/GeneratedExecuteShapeVerifier.cs:19`.
- Test coverage asserts these codes as contract-like outputs, including `tests/SafeIR.Tests/Verifier/Generated/VerifierTests.cs:13` through `:32`, `tests/SafeIR.Tests/Verifier/Core/VerifierAttackMatrixTests.cs:14` through `:20`, `tests/SafeIR.Tests/Verifier/Core/VerifierManifestIdentityTests.cs:70`, and `tests/SafeIR.Tests/Verifier/Generated/VerifierStackTypeTests.cs:29`.
- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:463` documents the verifier API shape, and `docs/Specs/Initial/safe-ir-sandbox-spec/spec/11-generated-code-verifier.md:27` mentions diagnostics in the result model, but neither file catalogs the emitted `V-*` codes or gives remediation guidance.
- A refreshed queue search found existing coverage for plugin analyzer diagnostic documentation (`API-0008`), sandbox error-code guidance (`CMP-0009`), verifier correctness findings such as `COR-0032`, verifier model mutability (`COR-0025`), and verifier performance findings, but no completeness/API finding for the public `SafeIR.Verifier` diagnostic-code reference.

## Impact

Consumers using the verifier package directly, or operators investigating compiled-artifact rejection through hosted/compiler paths, receive opaque `V-*` codes without a maintained public contract. That makes it hard to distinguish user-fixable shape problems from host/runtime version mismatches, package tampering, unsupported IL, or release-regression signals, and it lets new verifier codes ship without operator guidance.

## Better target

Add a public verifier diagnostics reference linked from `README.md`, `docs/Specs/Initial/safe-ir-sandbox-spec/spec/11-generated-code-verifier.md`, and `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md`. It should list every emitted `V-*` code or code family with meaning, likely cause, user/admin remediation, whether it indicates artifact tampering versus unsupported generated shape, and whether the code is expected from normal compiler output.

## Release gate idea

Add a docs readiness check that extracts `VerificationDiagnostic("V-...")` codes from `src/SafeIR.Verifier` and fails when the public reference lacks an entry for a new code or code family.

## Scope boundaries

This does not change verifier behavior, manifest identity validation, compiled-cache validation, error-code guidance, or plugin analyzer diagnostics. It is only about the missing public diagnostic reference for the `SafeIR.Verifier` package surface.

## Deduplication key

`api/verifier/diagnostic-reference/missing-public-docs`
