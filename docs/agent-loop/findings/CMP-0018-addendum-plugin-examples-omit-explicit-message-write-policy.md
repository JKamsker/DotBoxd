---
id: CMP-0018
area: completeness
status: verified
priority: medium
title: Addendum plugin examples omit explicit message-write policy
dedup_key: docs/addendum/plugin-message-policy/examples-missing-explicit-grant
created_at: 2026-06-12T23:19:39.3939858+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T00:21:33.9274350+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:05:57.1897557+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:10:34.1774675+00:00
fixed_commit: pending
verified_by: verifier
verified_at: 2026-06-13T00:21:33.9274350+00:00
verified_commit: 4fa49f1
duplicate_of: 
---

# CMP-0018: Addendum plugin examples omit explicit message-write policy

## Claim

The verified safe-default change for `PluginServer.Create()` removed the automatic `game.message.write` grant, but the public addendum walkthrough and runnable addendum/local plugin examples still install message-sending plugin packages with the default policy and no explicit message-write grant.

## Evidence

- `docs/agent-loop/findings/COR-0016-plugin-server-default-policy-grants-game-message-write-to-every-package.md` is `status: verified` and its fix direction was to make `PluginServer.Create()` default to a policy that does not automatically grant `game.message.write`.
- `src/DotBoxd.Plugins/PluginServer.cs:44` still registers the game-message binding with `builder.AddPluginMessageBindings(messages)`, but `src/DotBoxd.Plugins/PluginServer.cs:51` through `src/DotBoxd.Plugins/PluginServer.cs:55` now build the default policy with `GrantLogging()`, fuel, and host-call limits only. The default policy no longer calls `GrantGameMessageWrite()`.
- `docs/Specs/Addendum/Examples.md:259` still says "The default plugin server policy grants the safe message capability," and the nearby sample at `docs/Specs/Addendum/Examples.md:263` through `docs/Specs/Addendum/Examples.md:266` shows the old default policy shape with `GrantGameMessageWrite()`.
- The same walkthrough installs the fire-damage package with `PluginServer.Create(messages)` and `server.InstallAsync(FireDamagePluginPackage.Create())` at `docs/Specs/Addendum/Examples.md:115` through `docs/Specs/Addendum/Examples.md:116`, and later repeats `server.InstallAsync(package)` at `docs/Specs/Addendum/Examples.md:255` through `docs/Specs/Addendum/Examples.md:256` without passing an explicit policy.
- Runnable examples have the same pattern: `examples/LocalPlugin/DotBoxd.Kernels.PluginLocal/Program.cs:6` and `examples/LocalPlugin/DotBoxd.Kernels.PluginLocal/Program.cs:16`, `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/KernelClassExample.cs:11` and `:13`, `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/HookSubscriptionExample.cs:11` and `:14`, `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/RuntimeConfigurationExample.cs:11` and `:19`, and `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/ExecutionModeExample.cs:13` and `:14` all create a default server and install `FireDamagePluginPackage.Create()` without a policy override.
- `docs/Specs/Addendum/Examples.md:229` through `docs/Specs/Addendum/Examples.md:231` documents that the sample package requests `game.message.write`, and `tests/DotBoxd.Kernels.Tests/Misc06/PluginPackageJsonTests.cs:25` now asserts the default policy denies game-message write on JSON package install.

## Impact

The addendum docs and maintained examples no longer prove the public plugin workflow after the safe-default fix. Users copying the snippets can hit a policy failure during package preparation, while the documentation tells them the default server policy grants the capability. Release smoke can also miss whether the intended post-fix setup is to pass a per-install policy, construct the server with an explicit `defaultPolicy`, or use a read-only sample plugin for default-policy examples.

## Better target

Update the addendum walkthrough and runnable examples to make the capability boundary explicit. Message-sending examples should pass a named policy such as `SandboxPolicyBuilder.Create().GrantLogging().GrantGameMessageWrite().WithFuel(...).WithMaxHostCalls(...).Build()` either to `PluginServer.Create(defaultPolicy: ...)` or to `InstallAsync(..., policy: ...)`. Default-policy examples should use a package that does not request `game.message.write`.

## Release gate idea

Add or extend docs/example smoke so `AddendumExampleRunner.RunAsync()` and the local plugin example complete under the post-`COR-0016` default policy semantics. The smoke should fail if a message-sending package is installed without an explicit message-write grant, and the docs should no longer state that the default policy grants that capability.

## Scope boundaries

This does not reopen `COR-0016`; the source default-policy behavior is already verified. This finding covers the remaining user-facing docs/examples completeness gap after that behavior changed.

## Deduplication key

`docs/addendum/plugin-message-policy/examples-missing-explicit-grant`
