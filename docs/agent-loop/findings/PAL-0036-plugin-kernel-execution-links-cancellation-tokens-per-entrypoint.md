---
id: PAL-0036
area: perf_alloc
status: open
priority: medium
title: Plugin kernel execution links cancellation tokens per entrypoint
dedup_key: alloc/plugins/kernel-execution/linked-cancellation-source-per-entrypoint
created_at: 2026-06-12T23:07:51.5786388+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T23:07:51.5786388+00:00
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

# PAL-0036: Plugin kernel execution links cancellation tokens per entrypoint

## Claim

Plugin kernel execution creates a linked `CancellationTokenSource` for every sandbox entrypoint invocation, even on the common path where the caller supplies the default/non-cancelable token and the only active cancellation source is the kernel revocation token.

## Evidence

- `src/SafeIR.Plugins/InstalledKernel.cs:261` calls `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _revocation.Token)` inside `ExecutePreparedAsync`.
- `src/SafeIR.Plugins/InstalledKernel.cs:271` records telemetry after `_host.ExecuteAsync`, confirming the linked token source is on every prepared kernel execution path rather than only setup or error handling.
- `ShouldHandleAsync`, `HandleAsync`, and hook-driven `InvokeAsync` all route through `ExecutePreparedAsync`. A hook publish commonly runs `ShouldHandle` for each installed kernel and then `Handle` for accepted events, so the current path allocates one linked source and cancellation registrations per entrypoint execution.
- The revocation token is stable for the installed kernel lifetime. When the caller token cannot be canceled, passing `_revocation.Token` directly would preserve revocation behavior without allocating a linked source.
- Existing plugin performance findings cover execution observation retention (`PAL-0033`) and hook dispatch handler snapshots (`PAL-0035`). This finding is separate: it is cancellation infrastructure allocated before each sandbox execution.

## Impact

High-frequency plugin hosts can publish thousands of events per second. Each event may execute two kernel entrypoints per matching plugin, so unconditional linked-token creation adds avoidable Gen0 pressure and cancellation registration churn to the plugin execution hot path before any Safe-IR code runs.

## Better target

Introduce a small helper for plugin execution cancellation: use `_revocation.Token` directly when the caller token is not cancelable, use the caller token directly if revocation has already been handled or cannot cancel, and only create a linked token source when both independent tokens are cancelable. Keep the existing revocation semantics and disposal behavior for the linked case.

## Benchmark/allocation test idea

Add an allocation benchmark that publishes 1,000, 10,000, and 100,000 hook events through an installed kernel with the default cancellation token. Measure allocations before sandbox execution and assert the steady-state plugin path does not allocate a linked `CancellationTokenSource` per `ShouldHandle`/`Handle` call.
