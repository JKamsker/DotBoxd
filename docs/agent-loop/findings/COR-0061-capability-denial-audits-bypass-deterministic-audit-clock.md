---
id: COR-0061
area: correctness
status: open
priority: medium
title: Capability denial audits bypass deterministic audit clock
dedup_key: correctness:capability-denial-audit-deterministic-clock
created_at: 2026-06-13T06:28:39.3373067+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T06:28:39.3373067+00:00
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

# COR-0061: Capability denial audits bypass deterministic audit clock

## Evidence

`SandboxContext.RequireCapability` emits a `PolicyDenied` audit event with `DateTimeOffset.UtcNow` when policy denies a capability (`src/DotBoxd.Kernels/Sandbox/SandboxContext.cs`). The same context already has `AuditTimestamp()`, which uses the policy logical clock for deterministic policies, and other runtime bindings use that deterministic audit clock for their emitted events.

This is separate from synthesized `BindingCall` failure audits: the denial is emitted directly inside `RequireCapability` before the caller can normalize the timestamp.

## Impact

A deterministic execution policy can still produce wall-clock-dependent `PolicyDenied` audit events when a capability check fails. That breaks replayability and deterministic audit comparisons for hosts or custom bindings that call `RequireCapability` under a deterministic policy.

## Fix direction

Emit the `PolicyDenied` event through `AuditTimestamp()` instead of `DateTimeOffset.UtcNow`. Add a regression test that creates a deterministic policy with a logical clock, denies a capability through `RequireCapability`, and asserts that the audit timestamp equals the logical clock value.
