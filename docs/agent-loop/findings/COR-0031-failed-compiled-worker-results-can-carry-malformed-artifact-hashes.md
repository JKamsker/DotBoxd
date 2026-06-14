---
id: COR-0031
area: correctness
status: fixed_pending_verification
priority: medium
title: Failed compiled worker results can carry malformed artifact hashes
dedup_key: correctness/worker-result/failed-compiled-artifact-hash-unvalidated
created_at: 2026-06-12T22:33:57.4637511+00:00
created_by: codex-correctness-producer
created_commit: 
updated_at: 2026-06-13T00:38:12.3393463+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:36:40.3351307+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:38:12.3393463+00:00
fixed_commit: pending
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0031: Failed compiled worker results can carry malformed artifact hashes

## Summary
Host-side worker result validation accepts failed compiled-mode worker results with any `ArtifactHash` value. The mode validator skips artifact hash validation when `Succeeded` is false, and the run-summary validator also skips compiled envelope checks for failed results.

## Evidence
- `src/DotBoxd.Hosting/SandboxWorkerExecutor.cs` validates interpreted worker results by requiring a blank `ArtifactHash`, but for compiled results it returns `!result.Succeeded || IsHexSha256(result.ArtifactHash)`. A failed compiled result therefore bypasses the hex SHA-256 check entirely.
- The same file's run-summary validation returns `true` for compiled results when `!result.Succeeded`, so a failed compiled worker envelope does not have to prove that any artifact/hash fields are absent or well-formed.
- `tests/DotBoxd.Kernels.Tests/Misc08/WorkerResultHardeningTests.cs` covers malformed compiled runtime envelope fields for successful worker results, but it does not cover a failed compiled worker result that carries a malformed `ArtifactHash`.

## Why it matters
The worker boundary converts an out-of-process result into trusted public `SandboxExecutionResult` and audit evidence. Even on failure, accepting attacker-controlled or malformed artifact identity creates impossible public state for telemetry, cache diagnostics, hotness tracking, and downstream automation that assumes `ArtifactHash` is either absent or a valid artifact hash. This is separate from the existing worker error-code and forged audit-event findings because the malformed field is the top-level compiled artifact identity on a failed worker result.

## Suggested validation
Extend `WorkerResultHardeningTests` with a worker result where `Succeeded = false`, `ActualMode = ExecutionMode.Compiled`, `Error` uses a defined code, the run summary otherwise matches the failed result, and `ArtifactHash = "not-an-artifact-hash"`. The host should reject the worker envelope and return `WorkerIsolationFailed` with `SandboxErrorCode.HostFailure`. Add a second case if the intended contract is that failed compiled results must omit `ArtifactHash` entirely.

## Suggested fix
Make `WorkerModeMatches` validate `ArtifactHash` independently of success for compiled-mode worker results. Prefer a strict contract: interpreted results must have no artifact hash, successful compiled results must have a valid SHA-256 artifact hash, and failed compiled results must either omit the artifact hash or provide a valid SHA-256 hash consistently mirrored in the run-summary fields if those fields are present.
