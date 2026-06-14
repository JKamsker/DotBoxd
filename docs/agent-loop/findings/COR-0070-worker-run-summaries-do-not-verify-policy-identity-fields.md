---
id: COR-0070
area: correctness
status: open
priority: medium
title: Worker run summaries do not verify policy identity fields
dedup_key: correctness/worker-run-summary/policy-id/forged-or-missing
created_at: 2026-06-13T06:50:57.7170582+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T06:50:57.7170582+00:00
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

# COR-0070: Worker run summaries do not verify policy identity fields

## Claim

Host-side worker result validation accepts missing or forged `policyId` values in worker-supplied `RunSummary` audit fields even though in-process summaries always derive that field from the requested execution plan's policy.

## Evidence

- `src/DotBoxd.Kernels/Model/RunSummaryAuditFields.cs:24` emits `policyId` from `SafePolicyId(plan.Policy.PolicyId)` for host-created run summaries.
- `src/DotBoxd.Hosting/WorkerAuditValidator.cs:15` includes `policyId` in the allowed common worker run-summary field set, so a worker-supplied value is schema-accepted as long as the text is control-character-free.
- `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:233` starts `WorkerRunSummaryMatches(...)`, and the required field checks at `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:243` to `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs:246` compare `moduleHash`, `planHash`, `policyHash`, and `bindingManifestHash`, but never compare `policyId` against `RunSummaryAuditFields.Create(...)` or require the field to be present.
- `tests/DotBoxd.Kernels.Tests/Misc08/WorkerResultHardeningTests.cs:253` builds worker test summaries from `RunSummaryAuditFields.Create(...)`, and the hardening tests mutate resource aliases, dispatch state, error codes, and compiled envelope fields, but they do not cover a worker that changes or removes `policyId` while keeping `policyHash` correct.

## Impact

Worker-process isolation treats the worker result as an untrusted envelope. A compromised or buggy worker can publish audit evidence with a misleading human policy label, or omit the label entirely, while still satisfying the cryptographic policy hash checks. Audit consumers often use `policyId` for tenant, environment, or policy-name correlation, so the accepted result can misattribute a run even though the host knows the expected sanitized policy identity.

## Suggested fix

Make `WorkerRunSummaryMatches(...)` require `policyId` to equal the same sanitized value produced by `RunSummaryAuditFields.Create(plan, ...)`, or remove `policyId` from accepted worker-supplied fields and have the host rewrite it after validation. Add worker hardening tests for forged and missing `policyId` values under a non-default policy id.
