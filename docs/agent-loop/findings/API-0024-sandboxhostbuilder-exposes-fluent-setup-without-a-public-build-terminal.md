---
id: API-0024
area: api_coherence
status: open
priority: medium
title: SandboxHostBuilder exposes fluent setup without a public Build terminal
dedup_key: api/hosting/sandbox-host-builder/public-build-terminal-missing
created_at: 2026-06-13T06:44:37.7895048+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T06:44:37.7895048+00:00
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

# API-0024: SandboxHostBuilder exposes fluent setup without a public Build terminal

## Problem

`SandboxHostBuilder` is a public fluent builder with public configuration methods and an implicit public constructor, but its terminal `Build()` method is `internal`. Consumers can configure an instance directly but cannot turn that instance into a `SandboxHost`; they must route every build through `SandboxHost.Create(Action<SandboxHostBuilder>?)` instead.

This makes the package-facing host builder contract incomplete compared with adjacent public builders. `SandboxPolicyBuilder.Build()` and `BindingRegistryBuilder.Build()` are public terminals, while `SandboxHostBuilder.Build()` is not, even though the type itself is public.

## Affected public API

- `src/DotBoxd.Hosting/Execution/SandboxHostBuilder.cs`
- `src/DotBoxd.Hosting/Execution/SandboxHost.cs`
- `src/DotBoxd.Kernels/Policy.cs`
- `src/DotBoxd.Kernels/Bindings/BindingContracts.cs`

## Evidence

- `src/DotBoxd.Hosting/Execution/SandboxHostBuilder.cs:10` declares `public sealed class SandboxHostBuilder`, making the builder constructible by package consumers.
- `src/DotBoxd.Hosting/Execution/SandboxHostBuilder.cs:98` declares the only terminal method as `internal SandboxHost Build()`.
- `src/DotBoxd.Hosting/Execution/SandboxHost.cs:42` calls that internal terminal from `SandboxHost.Create(...)`, so direct consumers cannot reuse a configured builder instance outside the callback shape.
- Nearby package-facing builders expose public terminals: `src/DotBoxd.Kernels/Policy.cs:247` has `public SandboxPolicy Build()`, and `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:335` has `public BindingRegistry Build()`.

## Impact

Consumers cannot compose host configuration helpers that return a prepared `SandboxHostBuilder`, register the builder in DI and build later, or use the same construction style as other DotBoxd.Kernels builders. The public type suggests standalone builder semantics, but the terminal operation is hidden. That creates an API coherence gap rather than a runtime behavior bug.

## Recommendation

Pick one supported contract and make it explicit:

- If `SandboxHostBuilder` is intended to be a normal public builder, make `Build()` public and add a package/API test that constructs `new SandboxHostBuilder().AddDefaultPureBindings().UseInterpreter().Build()`.
- If the callback factory is the only supported construction model, hide direct builder construction, for example with an internal constructor or a clearer configuration-only type, so consumers are not handed an incomplete public builder.

## Non-duplicates checked

Existing API/CMP findings cover worker process setup, audit observer examples, compiler-cache examples, custom binding examples, and HTTP extension namespace issues. None track the source-level mismatch where the public host builder lacks a public terminal method while adjacent builders expose one.

## Acceptance criteria

- [ ] The supported host construction contract is explicit in source-level public API.
- [ ] Either `SandboxHostBuilder.Build()` is public and covered by a package/API test, or direct construction is hidden so only `SandboxHost.Create(...)` exposes the configuration callback model.
- [ ] Existing `SandboxHost.Create(...)` behavior remains source-compatible.
