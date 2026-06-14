---
id: CMP-0022
area: completeness
status: open
priority: medium
title: Custom host binding authoring lacks a package-backed public example
dedup_key: completeness/bindings/custom-host-binding/package-backed-example
created_at: 2026-06-13T06:24:02.9575109+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T06:24:02.9575109+00:00
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

# CMP-0022: Custom host binding authoring lacks a package-backed public example

## Claim

DotBoxd.Kernels exposes custom host binding authoring as public API, but there is no package-backed public example or docs smoke that creates a custom `BindingDescriptor`, registers it with `SandboxHostBuilder.AddBinding(...)`, grants the matching capability, executes JSON IR against it, and inspects the expected audit/resource result.

## Why this matters

Host-owned bindings are the main extensibility point for integrating DotBoxd.Kernels with product-specific data and services. Without a runnable consumer example, binding authors must infer the required cost model, capability grant validator, audit level, safety classification, and compiled-dispatch stub from specs or unit tests. Release validation can also miss a break in the public custom-binding workflow while default runtime bindings and plugin examples continue to pass.

## Evidence

- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:86` exposes `SandboxHostBuilder.AddBinding(BindingDescriptor descriptor)`.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:212` through `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:223` document `BindingRegistryBuilder` and show a `new BindingDescriptor(...)` shape.
- `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:3` exposes `BindingInvoker`, `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:7` exposes `CapabilityGrantValidator`, and `src/DotBoxd.Kernels/Bindings/BindingContracts.cs:58` exposes `BindingDescriptor`.
- `docs/Specs/Initial/dotboxd-sandbox-spec/checklists/release-readiness.md:62` marks the host binding author guide complete, but `scripts/check-docs-smoke.ps1:131` and `scripts/check-docs-smoke.ps1:132` only run addendum and local plugin examples before the optional IPC sample.
- A source search for `AddBinding`, `BindingDescriptor`, and `BindingInvoker` found custom binding usage in unit tests such as `tests/DotBoxd.Kernels.Tests/Misc02/CustomEffectBindingTests.cs`, but no matching README or `examples` project that a package consumer can run.

## Suggested test or smoke

Add a small package-backed custom binding example, for example `examples/CustomBinding`, that defines a binding such as `tenant.lookup`, supplies a `CapabilityGrantValidator`, registers it with `SandboxHost.Create(builder => builder.AddBinding(...))`, grants the capability through `SandboxPolicyBuilder.Grant(...)`, imports JSON IR that calls the binding, and asserts or prints the successful value plus binding audit fields. Wire it into `scripts/check-docs-smoke.ps1` or the package consumer smoke.

## Suggested fix direction

Promote one minimal test-only custom binding scenario into public docs/examples, with explicit guidance for safe defaults: required capability, deterministic cost model, audit level, `BindingSafety`, resource charging expectations, and the compiled runtime stub required for compiled mode. Keep the example small and package-oriented rather than source-tree-only.

## Scope boundaries

This is not about correctness bugs in binding validation, audit enforcement, or descriptor immutability; those are tracked by existing correctness findings. This finding is only about completing the public authoring surface and release proof for custom host bindings.

## Deduplication key

`completeness/bindings/custom-host-binding/package-backed-example`
