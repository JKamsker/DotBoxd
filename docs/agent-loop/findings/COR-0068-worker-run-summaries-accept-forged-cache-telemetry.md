---
id: COR-0068
area: correctness
status: open
priority: medium
title: Worker run summaries accept forged cache telemetry
dedup_key: correctness/worker-run-summary/cache-materialization-status/forged-telemetry
created_at: 2026-06-13T06:44:33.9064027+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T06:44:33.9064027+00:00
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

# COR-0068: Worker run summaries accept forged cache telemetry

## Claim

Worker-process run-summary validation accepts forged cache and materialization status telemetry as long as the field text is non-empty and otherwise safe.

## Evidence

- `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs` validates worker run summaries in `WorkerRunSummaryMatches(...)`, but it only checks `HasNonEmptyField(summary, "cacheStatus")`. For interpreted results it rejects compiled-only fields, but still accepts any cache status text. For compiled success it checks `artifactHash`, `runtimeForm`, and `cacheKey`, but it never validates `cacheStatus` or a present `materializationStatus` value.
- `src/DotBoxd.Hosting/WorkerAuditValidator.cs` explicitly whitelists `materializationStatus` as an allowed run-summary field and only requires field values to be control-character-free.
- In-process summaries are produced from trusted state: `src/DotBoxd.Hosting/Execution/CompiledExecutionRunner.cs` writes `artifact.CacheStatus.ToString()` and the cache materialization status into `RunSummaryAuditFields.Create(...)`. The worker boundary instead trusts the worker-supplied strings after the shallow checks above.

## Impact

A buggy or compromised worker can return an otherwise valid result and report impossible telemetry such as an interpreted run with `cacheStatus=Hit`, or a compiled run with a forged `materializationStatus`, and the host will publish the audit event as trusted. This undermines cache diagnostics and plugin execution observations while remaining distinct from existing findings about forged budget ceilings, malformed artifact hashes, and oversized success payloads.

## Better target

Validate worker summary cache telemetry against the same bounded states the host emits. Interpreted worker results should require `cacheStatus=None` and no materialization status. Compiled worker summaries should reject unknown cache status names and reject or normalize unknown `materializationStatus` values when that field is present. Add worker hardening tests for forged cache and materialization status fields.

## Deduplication key

`correctness/worker-run-summary/cache-materialization-status/forged-telemetry`
