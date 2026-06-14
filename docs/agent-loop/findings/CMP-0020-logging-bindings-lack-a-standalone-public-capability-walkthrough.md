---
id: CMP-0020
area: completeness
status: open
priority: medium
title: Logging bindings lack a standalone public capability walkthrough
dedup_key: completeness/logging/standalone-capability-walkthrough
created_at: 2026-06-12T23:30:04.3618094+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T23:30:04.3618094+00:00
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

# CMP-0020: Logging bindings lack a standalone public capability walkthrough

## Claim

DotBoxd.Kernels ships sandbox-visible logging bindings and policy/log quota APIs, but there is no standalone public walkthrough or runnable smoke that shows a host registering `log.info`/`log.warn`, granting `log.write`, applying log limits, and inspecting the resulting audit/resource output.

The current docs mention logging in package summaries, specs, and plugin defaults, but they do not prove the logging binding as a first-class runtime feature outside the plugin message examples.

## Why this matters

Logging is a user-facing safe API. Hosts need to know that logging requires both `AddLogBindings()` and `GrantLogging()`, that log messages are audited and sanitized, and that `WithMaxLogEvents` and `WithMaxLogMessageLength` are the intended controls. Without a runnable example, consumers can easily configure a module that calls `log.info` and get policy denials, or miss quota/redaction behavior that should be part of release validation.

## Evidence

- `src/DotBoxd.Kernels.Runtime/Bindings/DefaultSandboxBindings.cs:19` exposes `AddLogBindings` for the runtime binding catalog.
- `src/DotBoxd.Kernels.Runtime/Bindings/SafeLogBindings.cs:7` and `src/DotBoxd.Kernels.Runtime/Bindings/SafeLogBindings.cs:9` define `log.info` and `log.warn`; `src/DotBoxd.Kernels.Runtime/Bindings/SafeLogBindings.cs:18` requires `log.write`, and `src/DotBoxd.Kernels.Runtime/Bindings/SafeLogBindings.cs:42` records that capability in audit fields.
- `src/DotBoxd.Kernels/Policy.cs:146` exposes `GrantLogging`, while `src/DotBoxd.Kernels/Policy.cs:215` and `src/DotBoxd.Kernels/Policy.cs:221` expose the log event and message-length quotas.
- `README.md:11` lists logging as a shipped `DotBoxd.Kernels.Runtime` feature, but the minimal host sample does not call `AddLogBindings()` or `GrantLogging()`.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/04-ir-language.md:75` shows an isolated `log.info` IR expression, and `docs/Specs/Initial/dotboxd-sandbox-spec/spec/08-runtime-safe-apis.md:291` through `docs/Specs/Initial/dotboxd-sandbox-spec/spec/08-runtime-safe-apis.md:300` describes the safe logging API, but neither is a runnable public package example.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:85`, `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:186`, `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:197`, and `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md:198` list the API signatures without showing the end-to-end usage pattern.
- `examples/PluginIpc/DotBoxd.Kernels.PluginIpc.Server/PluginControlService.cs:36` grants logging only as part of a plugin server policy, so it does not demonstrate standalone sandbox logging behavior.

## Suggested test or benchmark

Add a docs-smoke fixture that imports JSON IR containing `log.info` and `log.warn`, configures a host with `AddLogBindings()`, grants `log.write`, and asserts successful audit/resource output. Include one tight quota case for `WithMaxLogEvents` or `WithMaxLogMessageLength` that returns the documented public failure shape.

## Suggested fix direction

Add a short logging section to the public docs or README and include its code path in the existing docs smoke script. The example should make the capability boundary explicit and show where hosts observe sanitized audit events and `ResourceUsage.LogEvents`.

## Scope boundaries

Do not fold this into the plugin message policy examples. The goal is a standalone runtime logging proof; plugin messaging and audit observer behavior have separate findings.

## Deduplication key

`completeness/logging/standalone-capability-walkthrough`

## Verification checklist

- [ ] Reproduction or test exists where practical.
- [ ] Fix addresses root cause.
- [ ] Relevant tests pass.
- [ ] Perf/allocation evidence exists where practical.
- [ ] No unrelated behavior changed.
