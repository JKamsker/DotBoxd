---
id: API-0012
area: api_coherence
status: open
priority: medium
title: Worker-process isolation has no shippable worker host surface
dedup_key: api/worker-process/missing-shipped-worker-host-sample
created_at: 2026-06-12T22:23:44.5002988+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T22:23:44.5002988+00:00
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

# API-0012: Worker-process isolation has no shippable worker host surface

## Claim

SafeIR exposes `SandboxIsolation.WorkerProcess`, `ISandboxWorkerClient`, `SandboxWorkerProfile.HardenedOutOfProcess`, and `SandboxHostBuilder.UseWorkerClient(...)` as public host APIs, but the repository does not ship a runnable worker-host implementation, package, or example that proves a real out-of-process boundary. Current coverage uses private in-process fake workers inside tests, so the advertised worker-process mode is effectively a bring-your-own protocol without a consumer-ready surface or release smoke.

## Why this matters

The public API and specs tell hosts to use a separate process/container/restricted account for hard isolation. Without a shipped worker host sample or adapter package, consumers must design their own serialization, process launch, cancellation, audit/result envelope validation, and profile hardening around a security-sensitive boundary. That makes `SandboxIsolation.WorkerProcess` look release-ready while the user-facing integration path remains incomplete and untested outside private test doubles.

## Evidence

- `src/SafeIR.Hosting/SandboxWorker.cs:5` exposes `ISandboxWorkerClient`, and `src/SafeIR.Hosting/SandboxWorker.cs:15` exposes `SandboxWorkerProfile` with `HardenedOutOfProcess` at `src/SafeIR.Hosting/SandboxWorker.cs:20`.
- `src/SafeIR.Hosting/Execution/SandboxHostBuilder.cs:88` exposes `UseWorkerClient(...)` as the host configuration hook, and `src/SafeIR.Core/ExecutionPlan.cs:108` exposes `SandboxIsolation.WorkerProcess`.
- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:520` through `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md:542` documents the worker API and says `SandboxIsolation.WorkerProcess` is an explicit deny-only mode until a host wires a hardened client.
- `docs/Specs/Initial/safe-ir-sandbox-spec/operations/runbook.md:27` says the boundary requires a worker client and `docs/Specs/Initial/safe-ir-sandbox-spec/README.md:29` recommends a separate process/container/restricted OS account for hard isolation.
- A targeted repo search found worker implementations only as private test doubles: `tests/SafeIR.Tests/Misc08/WorkerResultHardeningTests.cs:213` declares a private `TestWorker : ISandboxWorkerClient`, and `tests/SafeIR.Tests/Misc08/WorkerIsolationTests.cs:282` declares a private `CapturingWorker : ISandboxWorkerClient`.
- The shipped examples listed under `examples/` are Addendum, LocalPlugin, and PluginIpc; none contains a worker-process host/client sample or release-smoked worker executable.
- Existing worker findings cover envelope validation/correctness for worker results. This finding is separate: the public worker-process feature lacks a shippable user-facing integration surface and smoke proof.

## Suggested acceptance test

Add a small worker-process sample or package-level adapter and include it in docs/example smoke. The smoke should start a real child process or local transport boundary, configure `SandboxHostBuilder.UseWorkerClient(..., SandboxWorkerProfile.HardenedOutOfProcess)`, execute a minimal pure module with `Isolation = SandboxIsolation.WorkerProcess`, assert the result succeeds, then assert an unavailable/misprofiled worker fails closed.

## Suggested fix direction

Either ship a supported worker host/client adapter, for example a small stdio/named-pipe worker sample with documented envelope format and lifecycle, or clearly mark `ISandboxWorkerClient` as an advanced SPI and add a complete reference implementation under `examples/WorkerProcess`. The release gate should exercise that implementation so worker-process mode is proven outside private test doubles.

## Scope boundaries

Do not change the sandbox execution semantics or worker-result validation in this finding. The issue is the missing public integration surface and release-smoked example for the already-exposed worker-process API.

## Deduplication key

`api/worker-process/missing-shipped-worker-host-sample`
