---
id: ALG-0018
area: perf_algorithm
status: fixed_pending_verification
priority: medium
title: Direct plugin kernel dispatch revalidates adapter shape per call
dedup_key: algorithm/plugins/direct-kernel-dispatch/adapter-shape-validation-per-call
created_at: 2026-06-12T23:32:23.3047353+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T07:49:32.0958619+00:00
claimed_by: fixer
claimed_at: 2026-06-13T07:49:31.9697571+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-13T07:49:32.0958619+00:00
fixed_commit: b14fd0a
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# ALG-0018: Direct plugin kernel dispatch revalidates adapter shape per call

## Claim

Public direct plugin kernel dispatch reruns adapter and entrypoint-shape validation on every `ShouldHandleAsync` and `HandleAsync` call. The installed package, entrypoint IDs, live-setting definitions, and adapter shape are stable for a given kernel/adapter, but the direct path still rebuilds the expected parameter shape and rescans module functions before every sandbox execution.

## Evidence

- `src/DotBoxd.Plugins/InstalledKernel.cs:113` and `src/DotBoxd.Plugins/InstalledKernel.cs:134` call `ValidateFor(adapter)` on the direct public `ShouldHandleAsync` and `HandleAsync` paths before building input.
- `src/DotBoxd.Plugins/Runtime/KernelEntrypointValidator.cs:13` scans manifest subscriptions with `Any(...)` for each validation, then `src/DotBoxd.Plugins/Runtime/KernelEntrypointValidator.cs:20` calls `PluginParameterShape.BuildExpected(...)`.
- `src/DotBoxd.Plugins/Runtime/PluginParameterShape.cs:8` allocates a fresh `Parameter[]` sized to event parameters plus live settings, and `src/DotBoxd.Plugins/Runtime/PluginParameterShape.cs:16` creates live-setting `Parameter` entries on each call.
- `src/DotBoxd.Plugins/Runtime/KernelEntrypointValidator.cs:21` and `src/DotBoxd.Plugins/Runtime/KernelEntrypointValidator.cs:22` validate both `ShouldHandle` and `Handle`; each `ValidateFunction` uses `plan.Module.Functions.FirstOrDefault(...)`, so direct dispatch performs two linear module-function scans per call before comparing every parameter.
- `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:107` validates the same kernel/adapter shape once when adding a kernel to a hook pipeline, and `src/DotBoxd.Plugins/Runtime/PluginPreparedPackageValidator.cs:74` validates prepared entrypoints during install. The repeated direct-call validation is therefore not needed for the common stable adapter case.
- This is distinct from `ALG-0007`, which covers install-time package validation rescanning module functions; from `PAL-0004`, which covers convention adapter reflection/input conversion; and from `COR-0002`, which was the correctness issue requiring direct calls to fail closed for invalid adapters.

## Impact

Hosts that use the public direct `InstalledKernel.ShouldHandleAsync` and `HandleAsync` APIs for high-frequency events pay O(subscription-count + function-count + parameter-count) validation work plus a fresh expected-parameter array before every entrypoint execution. If the host calls `ShouldHandleAsync` and then `HandleAsync` separately for accepted events, the stable shape is rebuilt and the module is rescanned twice for the same event before sandbox code runs.

## Better target

Cache successful direct-call validation per installed kernel and adapter identity, or per event name plus immutable adapter parameter shape. Keep fail-closed behavior for unseen or changed adapters, but let repeated calls with the same validated shape reuse cached entrypoint/function metadata and expected parameters instead of rebuilding them. The hook pipeline can keep its existing validate-on-registration behavior.

## Benchmark/allocation test idea

Add a plugin direct-dispatch benchmark that invokes `ShouldHandleAsync` and `HandleAsync` 1,000 and 100,000 times with modules containing 10, 100, and 1,000 functions and adapters exposing 0, 5, and 20 event/live-setting parameters. Measure pre-execution time and allocated bytes before and after caching direct adapter-shape validation.
