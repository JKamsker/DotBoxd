---
id: API-0015
area: api_coherence
status: open
priority: medium
title: Generated runtime facade ships without a public support boundary
dedup_key: api/public-surface/generated-runtime-facade/support-boundary
created_at: 2026-06-12T22:37:46.9298422+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T22:37:46.9298422+00:00
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

# API-0015: Generated runtime facade ships without a public support boundary

## Claim

`DotBoxd.Kernels.Runtime` ships `CompiledRuntime` as an ordinary public static class even though the surrounding docs describe it as a compiler/verifier-owned runtime facade for generated assemblies. Because it is public, NuGet consumers can compile directly against dozens of low-level helpers such as metering, value construction, argument binding, collection mutation, and binding dispatch without any documented support boundary.

This is a specific package API readiness gap, not the general API compatibility gate from API-0009: the package currently exposes an implementation facade that must remain callable by generated assemblies, but there is no explicit decision or test that separates generated-code ABI from host-authored public API.

## Why this matters

Once shipped, consumers may treat `CompiledRuntime.*` as supported host API. That constrains future compiler/runtime refactors, verifier allowlist changes, metering semantics, and generated ABI evolution. It also gives users an attractive-looking way to bypass the intended host surface (`SandboxHost`, bindings, policies, and verified modules) even though direct calls are not documented as safe or stable.

## Evidence

- `src/DotBoxd.Kernels.Runtime/CompiledRuntime.cs:5` declares `public static class CompiledRuntime` in the packable `DotBoxd.Kernels.Runtime` package.
- The same file exposes many public helpers, including `ChargeFuel`, `ValidateEntrypointInput`, `GetInputArgument`, `TypeScalar`, `StringConst`, numeric operations, list/map operations, `CallBinding`, and `CreateValueArray`.
- `src/DotBoxd.Kernels.Verifier/VerifierTypeNames.cs:16` and `src/DotBoxd.Kernels.Verifier/VerificationPolicy.cs:32` identify `DotBoxd.Kernels.Runtime.CompiledRuntime` as the verifier-approved runtime facade for generated assemblies.
- `src/DotBoxd.Kernels/Bindings/BindingRegistryValidator.cs:6` and `:294` through `:295` hard-code `DotBoxd.Kernels.Runtime.CompiledRuntime` as the approved compiled binding target type.
- `README.md:5` frames compiled mode as compiler-owned generated runtime forms, but the package-facing README and package metadata do not mark `CompiledRuntime` as generated-code ABI, unsupported host API, or hidden from normal IntelliSense/API docs.

## Suggested test or benchmark

Add an API surface test or package consumer compile test that classifies public types by support tier. It should fail until `CompiledRuntime` is either explicitly documented as a generated-code ABI surface or hidden/segmented from normal host API discovery while remaining callable from generated assemblies.

## Suggested fix direction

Pick one support model and enforce it. Options include moving generated-code ABI helpers behind a clearly named namespace such as `DotBoxd.Kernels.Runtime.Generated`, adding `EditorBrowsable(EditorBrowsableState.Never)` plus explicit docs that only generated assemblies should call it, or splitting a tiny documented ABI package from host-facing runtime APIs. Keep verifier allowlists, cache hashing, and generated assemblies aligned with whatever ABI name/version is chosen.

## Scope boundaries

Do not remove the runtime facade or break verified generated assembly execution. Do not broaden arbitrary host calls or let user IR choose CLR members. This finding is only about making the generated-runtime ABI support boundary explicit and package-testable.

## Deduplication key

`api/public-surface/generated-runtime-facade/support-boundary`

## Verification checklist

- [ ] A public API/support-tier test covers generated-runtime ABI exposure.
- [ ] `CompiledRuntime` is documented, hidden, or segmented according to the chosen support model.
- [ ] Verifier allowlists and cache/runtime facade hashes stay synchronized with the chosen ABI.
- [ ] Existing compiled execution and verifier tests still pass.
