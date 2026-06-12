---
id: COR-0034
area: correctness
status: fixed_pending_verification
priority: high
title: Binding descriptors can mutate after manifest hash construction
dedup_key: correctness/binding-registry/descriptor-parameters/mutable-after-manifest-hash
created_at: 2026-06-12T22:29:27.3560557+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T23:18:14.0488555+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:13:16.2523974+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:18:14.0488555+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0034: Binding descriptors can mutate after manifest hash construction

## Claim

`BindingRegistry` exposes supposedly frozen binding descriptors whose parameter lists are mutable arrays, so binding metadata can change after `ManifestHash` has been computed and sealed into execution plans/cache keys.

## Evidence

- `src/SafeIR.Core/Bindings/BindingContracts.cs:53` defines public `BindingDescriptor` with an `IReadOnlyList<SandboxType> Parameters` property but the record itself does not snapshot or wrap that list.
- `src/SafeIR.Core/Bindings/BindingContracts.cs:111` computes `ManifestHash` once in the `BindingRegistry` constructor from the current `Signatures`.
- `src/SafeIR.Core/Bindings/BindingContracts.cs:132` exposes the persistent registry descriptor via `GetDescriptor(string id)`.
- `src/SafeIR.Core/Bindings/BindingContracts.cs:214` through `src/SafeIR.Core/Bindings/BindingContracts.cs:257` freeze descriptors by copying parameters into a `SandboxType[]`, but that array is stored as `IReadOnlyList<SandboxType>` and remains cast-mutable.
- `src/SafeIR.Core/Bindings/BindingContracts.cs:67` through `src/SafeIR.Core/Bindings/BindingContracts.cs:84` builds public signatures from the descriptor's current `Parameters`, so a mutation through `(SandboxType[])registry.GetDescriptor(id).Parameters` changes future `TryGet`/validation metadata while `ManifestHash` stays unchanged.
- `src/SafeIR.Core/Bindings/BindingContracts.cs:44` also exposes `BindingSignature.Parameters` without its own defensive-copy property, so signature snapshots returned from `TryGet` are backed by mutable arrays too, even though those arrays are transient copies.
- This is distinct from `COR-0028`: that finding covers mutating `SandboxType.Arguments`; this issue exists even when each `SandboxType` element is immutable because the binding parameter list itself remains mutable after registry construction.

## Impact

Binding manifest hash is part of plan identity and compiled-cache identity, but the registry can later report different parameter shapes under the same manifest hash. A host or plugin that obtains a descriptor can mutate its parameter array and make subsequent module validation, short-circuit analysis, compilation, or runtime descriptor lookups disagree with the manifest hash that was sealed into existing plans. This undermines binding catalog identity and can cause prepared plans or cache entries to represent a different binding ABI than later validations observe.

## Better target

Make `BindingDescriptor` and `BindingSignature` defensively copy parameter lists into non-cast-mutable read-only collections, and avoid returning persistent mutable descriptor internals from `GetDescriptor`. Add regression coverage that mutates the original descriptor parameter list and attempts to cast exposed `Parameters` collections from both `GetDescriptor` and `TryGet`, asserting `ManifestHash` and validation-visible signatures remain stable.
