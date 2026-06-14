---
id: COR-0032
area: correctness
status: fixed_pending_verification
priority: high
title: Direct verifier success does not require manifest identity binding
dedup_key: verifier/direct-api/manifest-identity-optional-for-tamper-evidence
created_at: 2026-06-12T22:35:17.3497558+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T23:51:18.2299427+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:50:11.0890366+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:51:18.2299427+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0032: Direct verifier success does not require manifest identity binding

## Claim

`GeneratedAssemblyVerifier.VerifyAsync` returns a successful verification for a generated assembly paired with a forged artifact manifest when the caller uses the default `VerificationPolicy` without `ExpectedManifestIdentity`. Only `AssemblyHash` is always checked; policy hash, plan hash, cache key, binding manifest hash, runtime facade hash, compiler/verifier versions, target framework, and optimization flags are skipped unless the caller remembered to wrap the policy with `WithExpectedManifest(...)`.

## Evidence

- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.cs` always checks `manifest.AssemblyHash` against the input bytes, then calls `ManifestIdentityVerifier.Verify(manifest, policy, diagnostics)`.
- `src/DotBoxd.Kernels.Verifier/Generated/ManifestIdentityVerifier.cs` immediately returns when `policy.ExpectedManifestIdentity` is null, so every manifest identity field other than the assembly hash is treated as informational.
- `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs` defaults `ExpectedManifestIdentity` to null, and `VerificationPolicy.BoxedValueDefaults()` does not set one.
- Hosted/compiler paths do set expected identity before materializing or reading cache entries, but `IGeneratedAssemblyVerifier` is a public verifier surface and the direct-verifier tests only prove rejection when callers opt in with `policy.WithExpectedManifest(...)`.

## Risk

A direct verifier consumer can verify bytes once, pair them with a manifest claiming a different policy hash, plan hash, cache key, binding manifest, or runtime facade, and still receive `VerificationResult.Succeeded == true`. That weakens the verifier as a tamper-evidence boundary: downstream release, cache, or audit tooling that trusts a successful verifier result plus the supplied manifest can record or accept forged provenance even though the assembly was never verified against that execution context.

## Suggested test

Add a direct verifier regression that builds a valid generated assembly, creates a current manifest, mutates `PolicyHash` or `CacheKey`, and calls `VerifyAsync(bytes, tamperedManifest, VerificationPolicy.BoxedValueDefaults(), CancellationToken.None)` without `WithExpectedManifest`. The test should fail on the current code because verification succeeds.

## Expected behavior

The verifier should fail closed when an artifact manifest is supplied without an expected identity, or it should expose a separate explicit API for shape-only verification whose result cannot be confused with manifest/provenance verification.

## Suggested fix direction

Require `ExpectedManifestIdentity` for manifest-bearing verification and emit a `V-MANIFEST-IDENTITY` diagnostic when it is absent, or split the API into two calls: one for IL/metadata shape verification and one for artifact-envelope verification. Keep compiler/cache/hosted paths passing the expected identity explicitly, and add tests proving direct verifier calls reject stale manifest identity by default.

## Deduplication key

verifier/direct-api/manifest-identity-optional-for-tamper-evidence
