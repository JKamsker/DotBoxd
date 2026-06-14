---
id: COR-0002
area: correctness
status: verified
priority: high
title: Direct plugin kernel invocation bypasses adapter whitelist validation
dedup_key: correctness/plugins/direct-kernel-invocation/adapter-whitelist-bypass
created_at: 2026-06-12T20:36:46.5010449+00:00
created_by: security-reviewer
created_commit: 
updated_at: 2026-06-12T20:54:13.2525759+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:39:33.9839646+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:40:52.9380059+00:00
fixed_commit: working-tree
verified_by: verifier
verified_at: 2026-06-12T20:54:13.2525759+00:00
verified_commit: 
duplicate_of: 
---

# COR-0002: Direct plugin kernel invocation bypasses adapter whitelist validation

## Claim

Public direct plugin kernel invocation bypasses the server-side event adapter whitelist and signature gate that hook registration enforces.

## Evidence

- `src/DotBoxd.Plugins/Runtime/HookRegistry.cs:107` calls `kernel.ValidateFor(_adapter)` before adding a kernel to a hook pipeline.
- `src/DotBoxd.Plugins/Runtime/KernelEntrypointValidator.cs:13` rejects adapters whose `EventName` is not present in `manifest.Subscriptions`, and it also validates the entrypoint parameter shape against the adapter and live settings.
- `src/DotBoxd.Plugins/InstalledKernel.cs:99` (`ShouldHandleAsync`) and `src/DotBoxd.Plugins/InstalledKernel.cs:118` (`HandleAsync`) accept an arbitrary `IPluginEventAdapter<TEvent>`, build input, and execute the prepared entrypoint without calling `ValidateFor(adapter)`.
- `src/DotBoxd.Plugins/InstalledKernel.cs:242` exposes the validation helper, but only the hook pipeline path uses it.
- `src/DotBoxd.Plugins/Runtime/PluginPreparedPackageValidator.cs:124` through `src/DotBoxd.Plugins/Runtime/PluginPreparedPackageValidator.cs:132` skips exact adapter parameter validation when the server has not registered an adapter shape for the manifest event. In that case, the direct public methods become the first runtime boundary that sees the caller-supplied adapter, but they do not enforce the manifest subscription or expected shape.

## Risk

DotBoxd.Kernels's plugin model treats hook subscriptions and server event adapters as the whitelist for which server events a plugin may observe. A host or integration that uses the public direct `InstalledKernel.ShouldHandleAsync` / `HandleAsync` helpers can execute a plugin for an event adapter that is not in the plugin manifest, as long as the adapter produces values compatible with the IR entrypoint. That bypasses the same verifier boundary enforced by `HookPipeline.UseKernel` and weakens explicit server whitelisting for direct invocation callers.

## Suggested acceptance tests

- Installing a package subscribed to `DamageEvent`, then calling `kernel.HandleAsync` with an adapter whose `EventName` is `AdminEvent` but whose values match the entrypoint parameter types, must throw `SandboxValidationException` with `DBXK031` and must not invoke side-effect bindings such as `game.message.send`.
- Calling `kernel.ShouldHandleAsync` with a subscribed event name but adapter `Parameters` that do not exactly match the installed entrypoints must throw `SandboxValidationException` with `DBXK033` before sandbox execution.
- Keep an existing hook-pipeline test showing `server.Hooks.On(adapter).UseKernel(kernel)` still validates once at registration and then executes normally.

## Expected behavior

Every public path that executes an installed plugin kernel with a caller-provided event adapter should enforce the same subscription and entrypoint-shape validation as hook registration before building sandbox input or executing IR.

## Suggested fix direction

Call `ValidateFor(adapter)` at the start of public `ShouldHandleAsync` and `HandleAsync`, before `BuildInput`. If repeated validation cost is a concern, cache successful `(kernel, adapter)` validations by adapter identity or by event name plus parameter shape, but keep fail-closed behavior for unregistered or mismatched adapters.

## Deduplication key

correctness/plugins/direct-kernel-invocation/adapter-whitelist-bypass
