---
id: COR-0009
area: correctness
status: fixed_pending_verification
priority: medium
title: Plugin adapter shape discovery crashes on explicit interface implementations
dedup_key: correctness/plugins/adapter-registry/explicit-interface-shape-crash
created_at: 2026-06-12T21:03:12.9666536+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T21:12:07.0798785+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:10:55.8738388+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:12:07.0798785+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0009: Plugin adapter shape discovery crashes on explicit interface implementations

## Claim

`PluginEventAdapterRegistry.TryResolveShape` can crash on a registered adapter that implements `IPluginEventAdapter<TEvent>` explicitly, because shape discovery reflects public properties on the concrete adapter type instead of reading through the interface instance.

## Evidence

`src/SafeIR.Plugins/Runtime/PluginEventAdapterRegistry.cs` stores registered adapters as `object`. `TryResolveShape` iterates `_adapters.Values` and calls `ReadShape(adapter)`. `ReadShape` uses `adapter.GetType().GetProperty(nameof(IPluginEventAdapter<object>.EventName))!` and the same pattern for `Parameters`, then immediately dereferences the result. Explicit interface implementations are valid C# implementations of the public `IPluginEventAdapter<TEvent>` contract, but their `EventName` and `Parameters` accessors are not public properties on the concrete adapter type, so `GetProperty(...)` returns null and `ReadShape` throws `NullReferenceException`.

The direct invocation and hook pipeline paths receive a typed `IPluginEventAdapter<TEvent>` and can read `adapter.EventName`/`adapter.Parameters` through the interface. The install-time prepared package validator is the path that uses `TryResolveShape`; a host that calls `server.RegisterEventAdapter(new ExplicitAdapter())` can therefore turn package installation into a host exception rather than either resolving the shape or producing a `SandboxValidationException` diagnostic.

Existing adapter tests cover convention discovery, duplicate event property names, adapter name mismatches, and different adapter instances, but they do not cover explicitly implemented adapter contracts.

## Risk

`RegisterEventAdapter<TEvent>` is a public host API. A valid adapter implementation style can break package installation with an unhandled host exception, which is a spec/API mismatch and weakens validation error quality around plugin installation.

## Suggested test

Add a test adapter that implements `IPluginEventAdapter<DamageEvent>` explicitly for `EventName`, `Parameters`, and `ToSandboxValues`. Register it with `server.RegisterEventAdapter(adapter)` and install a matching package. Installation should either succeed and validate against the adapter shape, or fail with a controlled `SandboxValidationException`; it should not throw `NullReferenceException` from `ReadShape`.

## Expected behavior

Shape discovery should work for all valid `IPluginEventAdapter<TEvent>` implementations accepted by `RegisterEventAdapter`, including explicit interface implementations.

## Suggested fix direction

Store enough typed adapter metadata at registration time, or update `ReadShape` to find the implemented `IPluginEventAdapter<>` interface and invoke its `EventName` and `Parameters` accessors through the interface map instead of concrete public-property reflection. If shape extraction fails, convert it to a `SandboxValidationException` diagnostic.

## Deduplication key

`correctness/plugins/adapter-registry/explicit-interface-shape-crash`
