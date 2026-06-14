---
id: PAL-0002
area: perf_alloc
status: verified
priority: medium
title: Live setting proxy reflects on every property access
dedup_key: alloc/plugins/live-context/proxy-reflection-per-access
created_at: 2026-06-12T20:36:53.2906670+00:00
created_by: perf-reviewer
created_commit: 
updated_at: 2026-06-12T21:29:04.1133732+00:00
claimed_by: fixer
claimed_at: 2026-06-12T21:25:31.1037206+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T21:26:59.1536883+00:00
fixed_commit: 
verified_by: independent-verifier
verified_at: 2026-06-12T21:29:04.1133732+00:00
verified_commit: working-tree
duplicate_of: 
---

# PAL-0002: Live setting proxy reflects on every property access

## Claim

Interface-shaped live settings use reflection and allocate argument arrays on every property get/set through `DispatchProxy`, instead of caching accessors per property/method.

## Evidence

- `LiveSettingStore.As<T>()` returns a `LiveContextProxy<T>` at `src/DotBoxd.Plugins/Runtime/LiveSettings.cs:130`.
- `LiveKernelValueFactory.Create<T>` also returns `kernel.Value.As<T>()` for interface-shaped settings at `src/DotBoxd.Plugins/Runtime/LiveKernelValueFactory.cs:9` and `src/DotBoxd.Plugins/Runtime/LiveKernelValueFactory.cs:10`.
- Every proxy invocation computes the property name at `src/DotBoxd.Plugins/Runtime/LiveContext.cs:33`, checks method-name strings at `src/DotBoxd.Plugins/Runtime/LiveContext.cs:34` and `src/DotBoxd.Plugins/Runtime/LiveContext.cs:38`, then routes to `Read`/`Write`.
- `Read` performs `typeof(LiveSettingStore).GetMethod(...).MakeGenericMethod(...).Invoke(...)` and allocates an object-array argument with `[propertyName]` at `src/DotBoxd.Plugins/Runtime/LiveContext.cs:49` through `src/DotBoxd.Plugins/Runtime/LiveContext.cs:52`.
- `Write` repeats the method lookup/generic construction/reflection invoke and allocates `[propertyName, value]` at `src/DotBoxd.Plugins/Runtime/LiveContext.cs:59` through `src/DotBoxd.Plugins/Runtime/LiveContext.cs:62`.
- Tests exercise interface-shaped kernel settings in `tests/DotBoxd.Kernels.Tests/Misc05/PluginAddendumTests.cs:157`, but there is no allocation benchmark for repeated live-setting property access.

## Impact

Live settings are part of plugin event handling and can be read in `ShouldHandle`/`Handle` paths. Interface-shaped settings currently pay repeated reflection metadata lookup, `MakeGenericMethod`, reflection invoke overhead, and argument-array allocation per property access. This matters when a plugin reads settings for every game/event hook invocation.

## Measurement idea

Add a BenchmarkDotNet benchmark for an interface settings type with several properties, measuring 100,000 get/set operations through `settings.Value`. Include allocated bytes and compare to class-shaped settings or cached delegates.

## Suggested fix direction

Cache per-`T` proxy metadata keyed by `MethodInfo`: property name, getter/setter kind, property type, and compiled delegates or non-generic `GetObject`/`SetObject` paths. Avoid per-call `GetMethod`, `MakeGenericMethod`, reflection `Invoke`, and fresh object arrays.
