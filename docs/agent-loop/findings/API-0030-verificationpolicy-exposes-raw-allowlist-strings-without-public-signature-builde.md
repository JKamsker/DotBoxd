---
id: API-0030
area: api_coherence
status: open
priority: medium
title: VerificationPolicy exposes raw allowlist strings without public signature builders
dedup_key: api/verifier/policy-allowlist/public-signature-builder-missing
created_at: 2026-06-13T06:58:45.8388477+00:00
created_by: core-completeness-producer
created_commit: 
updated_at: 2026-06-13T06:58:45.8388477+00:00
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

# API-0030: VerificationPolicy exposes raw allowlist strings without public signature builders

## Claim

`DotBoxd.Kernels.Verifier` exposes `VerificationPolicy` as a public customization point, but its allowlists are raw string sets and the helpers needed to construct correct type/member signatures are internal or private. Consumers can technically replace `AllowedTypes`, `AllowedMembers`, `AllowedAssemblyIdentities`, and `RuntimeFacadeIdentities`, but they must reverse-engineer the exact signature grammar and type-name vocabulary used by the verifier.

The public verifier policy contract is therefore incomplete for direct verifier/compiler integrations that need to extend or audit the allowlist safely.

## Evidence

- `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs` declares public `VerificationPolicy(...)` with public raw string allowlist properties such as `AllowedTypes`, `AllowedMembers`, `ForbiddenTypePrefixes`, and `RuntimeFacadeIdentities`.
- `VerificationPolicy.BoxedValueDefaults()` builds member allowlist entries with a private `RuntimeMember(string name, string parameters, string returnType)` helper.
- The canonical type names used by that helper live in `src/DotBoxd.Kernels.Verifier/VerifierTypeNames.cs`, whose containing type is `internal static class VerifierTypeNames`.
- Assembly identity formatting is also private to `VerificationPolicy` through `AssemblyIdentity(...)`, while `RuntimeFacadeIdentityDefaults()` is private.
- The verifier consumes these raw strings through `VerificationPolicy.IsMemberAllowed(...)`, `AllowlistHash`, and `RuntimeFacadeHash`, so an incorrectly formatted public allowlist silently changes verification/cache identity and blocks or admits members based on duplicated string conventions.

## Impact

A consumer implementing a custom compiler, verifier wrapper, or controlled runtime facade extension cannot build a policy using typed APIs. They must copy internal string formats like `DotBoxd.Kernels.Runtime.CompiledRuntime.Method(params):ReturnType`, CLR type names, assembly identity formats, and runtime facade identity rules. That makes the public `VerificationPolicy` extensibility brittle and hard to review, especially because policy hashes and artifact cache keys depend on these exact strings.

## Suggested fix direction

Add a small public policy construction surface. Options include a `VerificationPolicyBuilder`, public `VerificationMemberSignature` and `VerificationTypeName` helpers, or static factory methods for runtime members and assembly identities. The API should allow callers to start from `BoxedValueDefaults()`, add/remove allowed members with typed inputs, and compute the same allowlist/runtime-facade hashes without duplicating internal formatting rules.

## Non-duplicates checked

`API-0015` covers the support boundary of the public generated runtime facade. `API-0018` covers verifier diagnostic documentation. `API-0022` covers package API baseline extraction. Existing verifier correctness findings cover mutable policy collections and manifest identity validation. None cover the missing public builder/signature helpers required to use the existing `VerificationPolicy` customization surface.

## Deduplication key

`api/verifier/policy-allowlist/public-signature-builder-missing`

## Verification checklist

- [ ] Public code can construct allowed member signatures without copying internal string grammar.
- [ ] Public code can construct or extend verifier assembly/runtime facade identity inputs deliberately.
- [ ] Policy hashes remain stable for policies built through the new helper surface.
- [ ] Existing default verifier policy behavior remains unchanged.
