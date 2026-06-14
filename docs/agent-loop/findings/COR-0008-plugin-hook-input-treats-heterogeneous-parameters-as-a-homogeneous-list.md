---
id: COR-0008
area: correctness
status: rejected
priority: high
title: Plugin hook input treats heterogeneous parameters as a homogeneous list
dedup_key: correctness/plugins/hook-input/heterogeneous-parameters-homogeneous-list
created_at: 2026-06-12T21:02:03.6368887+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T21:02:31.5788507+00:00
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

# COR-0008: Plugin hook input treats heterogeneous parameters as a homogeneous list

## Claim

Installed plugin kernels build multi-argument hook inputs as a homogeneous `ListValue` using the first argument's type, so plugins with heterogeneous event parameters or live settings can fail at execution even though their entrypoint signatures validate successfully.

## Evidence

`src/DotBoxd.Plugins/InstalledKernel.cs` builds hook/direct-invocation input in `BuildInput`. When there is more than one event/live-setting value it calls `BuildInputList`. If there are only event values, `BuildInputList` returns `SandboxValue.FromList(eventValues, eventValues[0].Type)`. If live settings are appended, it fills a `SandboxValue[]` and returns `SandboxValue.FromList(values, values[0].Type)`.

Entrypoint binding for multi-parameter functions does not require a homogeneous list; `src/DotBoxd.Kernels/Model/EntrypointBinder.cs` only requires that multi-parameter input is a `ListValue` with the correct count, then validates each element against that parameter's expected type in `GetArgument`. Using the first argument's type as the list item type conflicts with that model when parameters are mixed, for example `String, I32, String`.

The plugin validation path allows mixed shapes: `KernelEntrypointValidator` compares adapter parameter names/types against the function parameters, and convention event adapters derive parameters from CLR property types. Existing tests include mixed convention events such as `ConventionDamageEvent(string DamageType, int Amount, string TargetId)` in `tests/DotBoxd.Kernels.Tests/Misc05/PluginHookSignatureTests.cs`, which demonstrates that heterogeneous plugin event parameter shapes are intended.

## Risk

A valid plugin package can install and pass hook signature validation, then fail at runtime when the event contains mixed parameter types or when live settings have a different type than the first event parameter. This is a spec/runtime mismatch in plugin dispatch and can make analyzer-generated plugins with ordinary mixed CLR event properties unusable.

## Suggested test

Add a plugin runtime test with an adapter exposing parameters `[String, I32, String]` and matching `ShouldHandle`/`Handle` entrypoints. Publish an event through `server.Hooks.On(adapter).UseKernel(kernel)` and assert the kernel handles it successfully. The test should fail before the fix because `BuildInputList` constructs a homogeneous list with item type `String` and the `I32` element does not match that list type during input metering/binding.

## Expected behavior

Plugin event/live-setting input should preserve positional argument values for heterogeneous entrypoint parameters. A valid adapter shape with mixed parameter types should execute the same way as any other multi-parameter DotBoxd.Kernels entrypoint.

## Suggested fix direction

Do not model multi-argument entrypoint input as a typed homogeneous user list when the list is only an argument tuple. Introduce or reuse an entrypoint tuple representation that `EntrypointBinder` can consume without requiring all items to share `values[0].Type`, or relax the internal multi-argument list validation path so each element is validated against its corresponding parameter type instead of the list item type.

## Deduplication key

`correctness/plugins/hook-input/heterogeneous-parameters-homogeneous-list`
