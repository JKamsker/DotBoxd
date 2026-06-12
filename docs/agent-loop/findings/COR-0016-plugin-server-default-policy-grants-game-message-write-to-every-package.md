---
id: COR-0016
area: correctness
status: verified
priority: high
title: Plugin server default policy grants game-message write to every package
dedup_key: security/plugins/default-policy/game-message-write-auto-grant
created_at: 2026-06-12T22:06:43.5140899+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T22:55:05.8238935+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:45:45.9513129+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T22:51:08.0719384+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T22:55:05.8238935+00:00
verified_commit: 
duplicate_of: 
---

# COR-0016: Plugin server default policy grants game-message write to every package

# Plugin server default policy grants game-message write to every package

## Summary

`PluginServer.Create()` installs the game-message binding and, unless the caller supplies a policy, grants `game.message.write` by default. That makes the production JSON upload path accept packages with `GameStateWrite` effects under default construction, even though plugin package manifests are untrusted input and capability requests should not become grants automatically.

## Evidence

- `src/SafeIR.Plugins/PluginServer.cs` builds the host with `builder.AddPluginMessageBindings(messages)` on every `PluginServer.Create()` call.
- The same method creates a default policy with `.GrantLogging().GrantGameMessageWrite().WithFuel(...).WithMaxHostCalls(...).Build()` when `defaultPolicy` is null.
- `src/SafeIR.Serialization.Json/PluginPackageJsonSerializer.cs` exposes `InstallJsonAsync(this PluginServer server, string json, SandboxPolicy? policy = null, ...)`; when callers use the default `policy: null`, the uploaded package is prepared under the server default policy.
- `tests/SafeIR.Tests/Misc06/PluginPackageJsonTests.cs` demonstrates `PluginServer.Create(messages)` followed by `server.InstallJsonAsync(JsonDamagePackage())`, where the JSON manifest declares `GameStateWrite` and calls `game.message.send`, and the install/run succeeds without an explicit per-package grant.

## Impact

A host following the simplest plugin server construction path grants all installed plugin packages the ability to emit game messages if their uploaded IR requests and declares that effect. This weakens the intended separation between plugin-authored package metadata and host-owned capability grants, and it makes the safe default for the higher-level plugin server more permissive than the core sandbox default-deny model.

## Test idea

Add a test that uses `PluginServer.Create(messages)` with no `defaultPolicy` and attempts to install a JSON package whose manifest/effects include `GameStateWrite` and whose module calls `game.message.send`. The safe-default expectation should be rejection with a policy diagnostic unless the test passes an explicit `SandboxPolicyBuilder.Create().GrantGameMessageWrite().GrantLogging().Build()` either to `PluginServer.Create(defaultPolicy: ...)` or to `InstallJsonAsync(..., policy: ...)`.

## Suggested fix

Make `PluginServer.Create()` default to a read/filter-safe policy, for example CPU/allocation plus logging only, and require hosts to opt in to `GrantGameMessageWrite` at server construction or per install. If the current permissive behavior is kept for demos, split it into a clearly named helper such as `CreateWithDemoMessageWriteDefaults` so production upload code does not inherit mutation privileges by default.
