---
id: COR-0033
area: correctness
status: verified
priority: high
title: SandboxPolicy grants can mutate after plan hashes are sealed
dedup_key: correctness/policy/grants/mutable-array-plan-hash-drift
created_at: 2026-06-12T22:29:25.9434588+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T23:22:17.9080673+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:13:14.8314599+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:18:12.4113447+00:00
fixed_commit: 
verified_by: codex-verifier
verified_at: 2026-06-12T23:22:17.9080673+00:00
verified_commit: 
duplicate_of: 
---

# COR-0033: SandboxPolicy grants can mutate after plan hashes are sealed

## Claim

`SandboxPolicy.Grants` is exposed as an `IReadOnlyList<CapabilityGrant>` but is backed by a mutable array, so callers can mutate a constructed policy's grants through a cast and change policy authorization/hash behavior after validation or plan construction.

## Evidence

- `src/DotBoxd.Kernels/Policy.cs:17` defines `SandboxPolicy` as a public record with a grants list constructor parameter.
- `src/DotBoxd.Kernels/Policy.cs:26` stores the list with `Grants.ToArray()` and exposes that same array as `IReadOnlyList<CapabilityGrant>` rather than wrapping it in `ReadOnlyCollection<CapabilityGrant>` or another immutable collection.
- Because arrays implement `IReadOnlyList<T>`, a caller can do `(CapabilityGrant[])policy.Grants` and replace entries after the policy has been built.
- `src/DotBoxd.Kernels/Policy.cs:28` recomputes `Hash` from the current grants, and `src/DotBoxd.Kernels/Policy.cs:33` through `src/DotBoxd.Kernels/Policy.cs:44` use the current grants for `GrantsCapability` and `GetGrant`.
- `src/DotBoxd.Hosting/Execution/ExecutionPlanBuilder.cs:17` through `src/DotBoxd.Hosting/Execution/ExecutionPlanBuilder.cs:25` hashes `policy.Hash` into a plan and stores both the original `PolicyHash` and the mutable `policy` reference in the `ExecutionPlan`.
- `src/DotBoxd.Hosting/Execution/ExecutionPlanGuard.cs:36` through `src/DotBoxd.Hosting/Execution/ExecutionPlanGuard.cs:52` later rebuilds expected plan identity from `plan.Policy`, so a post-prepare grants mutation can make a previously prepared plan fail integrity or make policy checks disagree with the sealed identity.
- Existing public immutability coverage in `tests/DotBoxd.Kernels.Tests/Misc06/PublicModelImmutabilityTests.cs` covers modules, plugin manifests, values, audit payloads, validation exceptions, and execution results, but it does not cover `SandboxPolicy.Grants`.

## Impact

A policy object is treated as stable identity input for validation, cache keys, plan seals, and runtime capability checks. Since its grants list can be modified after construction, policy hash and authorization behavior can drift without constructing a new policy. At minimum this lets ordinary public API use invalidate prepared plans unexpectedly; in concurrent hosts it can also make validation, hashing, and execution observe different grant sets for the same policy instance.

## Better target

Store grants through the same defensive-copy pattern used by other public models, for example `ModelCopy.List(Grants)` or a private immutable/read-only collection field. Add a public immutability regression test that attempts to cast `policy.Grants` back to `CapabilityGrant[]`, mutates the original constructor list and any exposed concrete collection, and verifies policy grants/hash behavior remain stable.
