---
id: CMP-0011
area: completeness
status: claimed
priority: medium
title: README lacks a safe-default host baseline before IO examples
dedup_key: cmp/samples/safe-default-host-baseline-missing
created_at: 2026-06-12T22:08:16.1396303+00:00
created_by: continuous-completeness-producer
created_commit: 
updated_at: 2026-06-13T06:16:31.5203443+00:00
claimed_by: worker
claimed_at: 2026-06-13T06:16:31.5203443+00:00
claim_branch: workflow-work
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# CMP-0011: README lacks a safe-default host baseline before IO examples

## Claim

The README and public API docs lead with host samples that immediately register file bindings, file grants, and compiler support, but they do not provide a deny-by-default/pure-computation sample that demonstrates the safest starting posture before adding IO or compiled execution.

## Why this matters

DotBoxd.Kernels's security model is capability-based. New hosts need an obvious minimal configuration that has no file/network/time/random bindings, explicit resource limits, and no optional compiled/runtime cache setup until they intentionally opt in. Without that sample, onboarding starts from a broader host surface than necessary.

## Evidence

- `README.md:22` introduces `Minimal Host Usage`.
- `README.md:30` through `README.md:33` configure `AddDefaultPureBindings`, `AddFileBindings`, `UseInterpreter`, and `UseCompilerIfAvailable` in that minimal sample.
- `README.md:38` grants file read access and `README.md:39` sets fuel, so the README's first host policy example includes IO rather than showing a pure deny-by-default baseline first.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:8` through `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:13` also lead with file bindings, compiler support, compiler cache, and audit forwarding in the high-level usage sample.
- Searches for `safe default` in README/examples did not find a dedicated safe-default host sample.

## Suggested acceptance test

Add a docs smoke fixture that compiles and runs a `SafeDefaults` sample using only `AddDefaultPureBindings`, `AllowPureComputation`, explicit fuel/wall-time/resource limits, and interpreted execution. The fixture should fail if the sample registers file/network bindings or grants IO capabilities.

## Suggested fix direction

Add a README and/or `examples` safe-default host sample before the IO sample. Show pure computation with explicit limits first, then a separate opt-in section for file, network, compiler/cache, worker, and audit-retention configuration.

## Scope boundaries

Do not remove the existing file-read example. This finding only asks for an additional safest-baseline sample and smoke coverage.

## Deduplication key

`cmp/samples/safe-default-host-baseline-missing`
