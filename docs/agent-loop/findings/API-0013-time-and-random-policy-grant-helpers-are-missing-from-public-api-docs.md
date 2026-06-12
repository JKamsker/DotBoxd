---
id: API-0013
area: api_coherence
status: open
priority: medium
title: Time and random policy grant helpers are missing from public API docs
dedup_key: api/policy-builder/time-random-grant-helpers-missing-from-public-docs
created_at: 2026-06-12T22:26:52.2836240+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T22:26:52.2836240+00:00
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

# API-0013: Time and random policy grant helpers are missing from public API docs

## Claim

The public API reference and README package guidance expose time and random bindings on `SandboxHostBuilder`, but they do not document the matching public policy grant helpers `SandboxPolicyBuilder.GrantTimeNow()` and `GrantRandom()`. Consumers can discover `AddTimeBindings()` and `AddRandomBindings()` but not the intended safe policy setup needed to prepare modules that request `time.now` or `random`.

## Why this matters

Time and random are capability-gated runtime features. If the public policy-builder surface omits the dedicated grant helpers, host authors are pushed toward the generic `Grant(...)` escape hatch or source/tests spelunking, which weakens the user-facing capability model and makes deterministic setup harder to copy correctly.

## Evidence

- `README.md:11` says `SafeIR.Runtime` includes safe host bindings for time and random.
- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:83` and `:84` list `SandboxHostBuilder.AddTimeBindings()` and `AddRandomBindings()` as public setup APIs.
- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:175` through `:201` list `SandboxPolicyBuilder` methods, but include only file grants, `GrantLogging()`, generic `Grant(...)`, and `Deterministic(...)`; they omit `GrantTimeNow()` and `GrantRandom()`.
- `src/SafeIR.Core/Policy.cs:130` defines the public `GrantTimeNow()` helper, and `src/SafeIR.Core/Policy.cs:137` defines the public `GrantRandom()` helper.
- `tests/SafeIR.Tests/Misc07/TimeAndRandomTests.cs:26` and `:45` use those helpers to make documented time/random modules prepare and run, proving they are intended public behavior rather than test-only internals.
- Existing findings API-0001 through API-0012 and CMP-0001 through CMP-0012 cover internal namespaces, package metadata/readme, consumer smoke, diagnostics, API compatibility, symbols/source-link, prerelease IPC, worker host surface, operational docs, error-code reference, safe-default samples, and JSON schema artifacts; none track the missing time/random grant-helper surface.

## Suggested acceptance test

Add a consumer-facing docs/API compile fixture that imports `SafeIR`, `SafeIR.Hosting`, and `SafeIR.Runtime`, then verifies this setup compiles and prepares modules requesting time and random without using generic `Grant(...)`:

```csharp
using var host = SandboxHost.Create(builder => {
    builder.AddDefaultPureBindings();
    builder.AddTimeBindings();
    builder.AddRandomBindings();
});

var policy = SandboxPolicyBuilder.Create()
    .GrantTimeNow()
    .GrantRandom()
    .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 123)
    .Build();
```

## Suggested fix direction

Update the public API reference and relevant runtime/capability docs to list and explain `GrantTimeNow()` and `GrantRandom()`, including the deterministic `LogicalNow`/`RandomSeed` relationship. Add a small README or example snippet if time/random are intended as shipped runtime features, and keep a docs smoke/compile check synchronized with the policy-builder public method list.

## Scope boundaries

Do not redesign policy grants or capability validation as part of this finding. This is about documenting and proving the existing public helper surface for shipped time/random runtime bindings.

## Deduplication key

`api/policy-builder/time-random-grant-helpers-missing-from-public-docs`

## Verification checklist

- [ ] Public API docs list `GrantTimeNow()` and `GrantRandom()` next to the other policy-builder helpers.
- [ ] Runtime/capability docs explain which capability IDs those helpers grant and how they interact with deterministic execution.
- [ ] A consumer-facing compile/docs smoke fixture proves `AddTimeBindings()`/`AddRandomBindings()` plus the grant helpers prepare time/random modules.
- [ ] Generic `Grant(...)` remains available but is not the only documented path for time/random capability setup.
