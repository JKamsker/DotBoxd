---
id: COR-0018
area: correctness
status: verified
priority: high
title: Plugin kernel revocation does not fence already-running handlers
dedup_key: security/plugins/revocation/in-flight-handler-not-fenced
created_at: 2026-06-12T22:10:50.6342941+00:00
created_by: continuous-security-producer
created_commit: 
updated_at: 2026-06-12T22:55:07.1159057+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:45:47.2276888+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T22:51:09.3639210+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T22:55:07.1159057+00:00
verified_commit: 
duplicate_of: 
---

# COR-0018: Plugin kernel revocation does not fence already-running handlers

## Claim

Plugin kernel revocation is only checked before selected entrypoint calls, so a direct or already-dispatched handler can continue performing privileged plugin side effects after the kernel is uninstalled or replaced.

## Evidence

`src/DotBoxd.Plugins/InstalledKernel.cs` implements `Revoke()` as a flag write and exposes `IsRevoked`, but it does not create or cancel a per-kernel cancellation token. `HandleAsync` and `ShouldHandleAsync` acquire `_executionGate`, call `PluginKernelRevocation.ThrowIfRevoked(IsRevoked)`, and then run `ExecutePreparedAsync`. Once execution has entered `ExecutePreparedAsync`, there is no second revocation check and no cancellation token tied to `Revoke()`.

`InvokeAsync` has a special hook-pipeline check after `ShouldHandle` and before `Handle`, which covers uninstall between filter and handler, but it still does not fence a handler that is already executing. `PluginServer.Uninstall` and `KernelRegistry.Add` call `kernel.Revoke()` on stale kernels, but the active sandbox run keeps the original caller cancellation token and plan.

Existing coverage in `tests/DotBoxd.Kernels.Tests/Misc06/PluginRevocationTests.cs` covers uninstall before a later hook publish, reinstall revoking a pipeline-captured kernel, direct execution after prior revocation, and uninstall between `ShouldHandle` and `Handle`. It does not cover revocation while `Handle` is already blocked inside a host binding before reaching `game.message.send`.

A concrete repro shape is a plugin `Handle` entrypoint that calls a blocking host binding and then calls `game.message.send`. Start `kernel.HandleAsync`, wait until the blocking binding is entered, call `server.Uninstall(pluginId)`, release the binding, and the stale handler can still emit the game message because the revocation flag is not consulted again.

## Impact

Revocation and uninstall are expected control-plane operations for stopping a plugin's capability to act. A stale in-flight direct execution can still publish game messages or perform any other capability granted to its prepared plan after the host believes the plugin has been revoked. This is a time-of-check/time-of-use revocation gap for privileged plugin actions.

## Security test idea

Add a `PluginRevocationTests` case with a blocking binding at the beginning of `Handle`, followed by `game.message.send`. Start `HandleAsync`, wait for the blocking binding, uninstall the plugin, release the binding, and assert that no message is emitted and the task fails or is cancelled with `PolicyDenied`.

## Suggested fix direction

Give each `InstalledKernel` a revocation `CancellationTokenSource` or generation fence. Link that token into every `ExecutePreparedAsync` call, cancel it in `Revoke()`, and optionally re-check the generation before side-effecting plugin bindings publish externally visible effects. Keep the existing pre-entrypoint checks as fast fail behavior, but do not rely on them as the only revocation boundary.
