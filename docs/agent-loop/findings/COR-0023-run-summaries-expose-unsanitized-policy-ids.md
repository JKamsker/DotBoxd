---
id: COR-0023
area: correctness
status: fixed_pending_verification
priority: medium
title: Run summaries expose unsanitized policy IDs
dedup_key: security/audit/run-summary/policy-id-unredacted
created_at: 2026-06-12T22:17:55.9444967+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-13T00:21:35.2479073+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:18:17.8042832+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:21:35.2479073+00:00
fixed_commit: pending
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0023: Run summaries expose unsanitized policy IDs

## Claim

Host-provided sandbox policy IDs are copied into public run-summary audit fields and messages without validation, redaction, or control-character cleanup.

## Evidence

`src/DotBoxd.Kernels/Policy.cs` exposes `SandboxPolicyBuilder.WithPolicyId(string policyId)` and assigns the value directly to `_policyId`. `Build()` then stores it in `SandboxPolicy.PolicyId` without checking for control characters, path/secret-shaped values, or maximum length. Callers can also construct a `SandboxPolicy` record directly with any `PolicyId` string.

`src/DotBoxd.Kernels/Model/RunSummaryAuditFields.cs` writes that value into the public run-summary field dictionary as `fields["policyId"] = plan.Policy.PolicyId`. `src/DotBoxd.Hosting/Execution/CompiledExecutionRunner.cs` also embeds the same value in the compiled run-summary message with `policyId={plan.Policy.PolicyId}`, and `src/DotBoxd.Hosting/SandboxHost.Results.cs` embeds it in failed run-summary messages. None of those paths call `AuditTextSanitizer`, reject control characters, or redact secret-like tokens.

This is distinct from the existing path-resource and revocation-reason audit findings: the leaked value is host-owned policy metadata that appears on every run summary, not a sandbox path resource or revocation reason.

## Risk

Hosts often use policy IDs to correlate tenant, environment, or authorization configuration. A policy ID such as `tenant-prod-api-key-...`, a filesystem path, or a value containing newlines is propagated to public `SandboxExecutionResult.AuditEvents`, forwarded audit observers, and any downstream telemetry exporter. That creates a diagnostics-leakage and log-injection surface even when individual binding audit messages are redacted.

## Suggested test

Add coverage that builds or directly constructs a policy with a policy ID containing a secret-shaped token and a newline, executes any entrypoint that emits a run summary, and asserts that the `RunSummary` message and `Fields["policyId"]` either reject the policy up front or contain only sanitized/redacted text.

## Expected behavior

Policy IDs should be treated as diagnostic labels with a safe grammar: reject empty/control-character values and either require an opaque identifier grammar or sanitize/redact before writing them to audit messages and fields.

## Suggested fix direction

Validate `PolicyId` in `SandboxPolicyBuilder.WithPolicyId` and in the `SandboxPolicy` construction path used by public callers, or centralize a `PolicyId` value object/helper that enforces an opaque ID grammar. For defense in depth, run-summary writers should not interpolate unsanitized policy metadata into free-form messages.

## Deduplication key

`security/audit/run-summary/policy-id-unredacted`
