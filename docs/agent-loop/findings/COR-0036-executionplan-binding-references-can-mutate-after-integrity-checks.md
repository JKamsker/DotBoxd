---
id: COR-0036
area: correctness
status: verified
priority: medium
title: ExecutionPlan binding references can mutate after integrity checks
dedup_key: correctness:executionplan-bindingreferences-mutable-after-guard
created_at: 2026-06-12T22:48:34.1123907+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-12T23:22:21.1773750+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:13:17.8378548+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:18:16.1715325+00:00
fixed_commit: 
verified_by: codex-verifier
verified_at: 2026-06-12T23:22:21.1773750+00:00
verified_commit: 
duplicate_of: 
---

# COR-0036: ExecutionPlan binding references can mutate after integrity checks

## Claim

`ExecutionPlan.BindingReferences` exposes mutable `HashSet<string>` instances through an `IReadOnlySet<string>` facade, so a prepared plan's per-entrypoint binding allowlist can be changed after construction and after integrity comparison.

## Evidence

`src/SafeIR.Core/ExecutionPlan.cs` builds `BindingReferences` with `CopyBindingReferences`, but each copied value is `new HashSet<string>(...)` stored directly as `IReadOnlySet<string>` inside the public `ReadOnlyDictionary`. A caller can cast `plan.BindingReferences[entrypoint]` back to `HashSet<string>` and add or remove binding IDs.

`src/SafeIR.Hosting/Execution/ExecutionPlanGuard.cs` compares `plan.BindingReferences` against a freshly built expected plan during `EnsurePrepared`, but the runner then passes the same mutable set into execution. `src/SafeIR.Interpreter/SandboxInterpreter.cs` and `src/SafeIR.Hosting/Execution/CompiledExecutionRunner.cs` both read `plan.BindingReferences` to seed the allowed binding IDs used by `SandboxContext.ChargeBindingCall`.

## Impact

The execution plan is intended to be a sealed artifact tying module, policy, binding manifest, function analysis, and reachable binding references together. Because the exposed binding-reference sets remain mutable, code with a prepared plan reference can race or otherwise alter the allowed binding IDs after the integrity check and before/during execution, making the runtime binding fence disagree with the validated module graph.

This is not the same as `COR-0024`: that finding covers mutable collections returned by `ModuleValidationResult`. This finding covers the public `ExecutionPlan` copy boundary and the execution-time allowlist consumed by the interpreter and compiled runner.

## Suggested fix

Freeze the per-entrypoint sets when constructing an `ExecutionPlan`, for example by storing `ReadOnlySet<string>`, `FrozenSet<string>`, or immutable arrays plus set lookup helpers instead of concrete mutable `HashSet<string>` values. Add a public-model immutability test that attempts to cast and mutate `plan.BindingReferences["main"]`, then asserts the plan's binding allowlist cannot change.
