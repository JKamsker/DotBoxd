---
id: COR-0029
area: correctness
status: open
priority: high
title: VerificationPolicy allowlists can mutate after cache-key construction
dedup_key: correctness/verifier/policy/mutable-allowlist-cache-verification-drift
created_at: 2026-06-12T22:25:04.4795505+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T22:25:04.4795505+00:00
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

# COR-0029: VerificationPolicy allowlists can mutate after cache-key construction

## Claim

`VerificationPolicy` keeps caller-owned mutable allowlist sets, so the verifier policy used for cache-key identity and assembly verification can be changed after construction or while a compiler/cache instance is using it.

## Evidence

- `src/SafeIR.Verifier/VerificationPolicy.cs:9` through `src/SafeIR.Verifier/VerificationPolicy.cs:15` declares the public policy as a record over six `IReadOnlySet<string>` allowlist/denylist collections with no snapshotting constructor or init accessors.
- `src/SafeIR.Verifier/VerificationPolicy.cs:116` checks allowed members directly against the live `AllowedMembers` set.
- `src/SafeIR.Verifier/VerificationPolicy.cs:121` through `src/SafeIR.Verifier/VerificationPolicy.cs:135` recomputes `AllowlistHash` and `RuntimeFacadeHash` from the live sets every time the properties are read.
- `src/SafeIR.Compiler/CacheKeyBuilder.cs:20` through `src/SafeIR.Compiler/CacheKeyBuilder.cs:37` includes those live hashes in compiled cache keys, and `src/SafeIR.Compiler/CacheKeyBuilder.cs:47` through `src/SafeIR.Compiler/CacheKeyBuilder.cs:59` embeds the runtime facade hash in artifact manifest identity.
- `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:91` through `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:114` validates assembly/type references against `AllowedAssemblies`, `AllowedAssemblyIdentities`, `AllowedTypes`, and `ForbiddenTypePrefixes`; `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:123` through `src/SafeIR.Verifier/Generated/GeneratedAssemblyVerifier.cs:129` validates member references through `IsMemberAllowed`.
- `src/SafeIR.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs:13` through `src/SafeIR.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs:22` stores a caller-supplied `VerificationPolicy` instance for later compile/cache use.
- Existing cache identity tests mutate policy by creating replacement sets with `with`, for example `tests/SafeIR.Tests/Misc01/CacheKeyIdentityTests.cs:16` through `tests/SafeIR.Tests/Misc01/CacheKeyIdentityTests.cs:24`; they do not cover aliasing a mutable set after policy construction.

A minimal repro shape is:

```csharp
var members = VerificationPolicy.BoxedValueDefaults().AllowedMembers.ToHashSet(StringComparer.Ordinal);
var policy = VerificationPolicy.BoxedValueDefaults() with { AllowedMembers = members };
var before = policy.AllowlistHash;
members.Add("SafeIR.Runtime.CompiledRuntime.TestOnly(SafeIR.SandboxContext):System.Void");
Assert.NotEqual(before, policy.AllowlistHash);
Assert.True(policy.IsMemberAllowed("SafeIR.Runtime.CompiledRuntime.TestOnly(SafeIR.SandboxContext):System.Void"));
```

## Impact

The compiled cache key and manifest identity are supposed to pin the verifier allowlist that authorized an artifact. With mutable set aliases, a policy instance can produce one cache key/hash at one point and verify using a different allowlist later. A host that reuses one compiler/cache policy object across runs can therefore get nondeterministic cache identities and verification decisions depending on external mutations or races against the caller-owned sets.

This is distinct from COR-0025: that finding covers mutable verifier manifest/result payload collections returned with artifacts. This finding covers the verifier policy input itself, which controls allowlist decisions and cache identity.

## Better target

Snapshot all `VerificationPolicy` collection inputs on construction and `with` init, preferably into immutable/read-only sets with ordinal comparers preserved. Add tests that mutate the original sets after policy construction and after `with` updates, then assert `AllowlistHash`, `RuntimeFacadeHash`, and `IsMemberAllowed` remain stable.
