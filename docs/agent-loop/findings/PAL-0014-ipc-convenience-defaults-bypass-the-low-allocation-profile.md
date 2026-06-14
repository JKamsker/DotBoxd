---
id: PAL-0014
area: perf_alloc
status: open
priority: low
title: IPC convenience defaults bypass the low-allocation profile
dedup_key: alloc/ipc-dotboxd/default-options/low-allocation-profile-disabled
created_at: 2026-06-12T22:07:32.7900775+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:07:32.7900775+00:00
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

# PAL-0014: IPC convenience defaults bypass the low-allocation profile

## Claim

The DotBoxd.Kernels DotBoxd MessagePack IPC convenience defaults leave the low-allocation unary invocation path disabled, so callers using the named-pipe helpers get the higher-allocation transport profile unless they manually know which DotBoxd options to override.

## Evidence

- `src/DotBoxd.Pushdown.Services/DotBoxdDotBoxdRpcMessagePackIpc.cs:10` defines `DefaultClientOptions` with `RequestTimeout` and `RejectInboundCalls`, but it does not set `EnableLowAllocationValueTaskInvocations`.
- `src/DotBoxd.Pushdown.Services/DotBoxdDotBoxdRpcMessagePackIpc.cs:14` defines bidirectional client defaults with only `RequestTimeout`.
- `src/DotBoxd.Pushdown.Services/DotBoxdDotBoxdRpcMessagePackIpc.cs:23` passes server options through as provided, so default `ListenNamedPipe` callers also do not get the low-allocation server-side options used by the benchmarks.
- `benchmarks/DotBoxd.Kernels.Benchmarks/Ipc/IpcAllocationProfile.cs:76` enables `EnableLowAllocationValueTaskInvocations` only when the explicit `--low-alloc` profile is selected, and `benchmarks/DotBoxd.Kernels.Benchmarks/Ipc/IpcAllocationProfile.cs:89` separately opts the server into low-allocation settings.
- `benchmarks/DotBoxd.Kernels.Benchmarks/Program.cs:8` exposes this only as a manual profile mode; there is no allocation regression gate for the public DotBoxd.Kernels IPC defaults.

## Impact

DotBoxd.Kernels exposes IPC as a preview addon with named-pipe convenience helpers, but the easy path is not the low-allocation path already identified by the benchmark harness. Plugin IPC users can pay avoidable per-call allocation on request/response dispatch while believing they are using the recommended DotBoxd.Kernels transport defaults.

## Better target

Either make the DotBoxd.Kernels IPC defaults use the low-allocation DotBoxd options when the safety tradeoffs are acceptable, or expose an explicit `LowAllocation` DotBoxd.Kernels options factory and document/gate it. The target should make the public convenience path allocation behavior intentional and measured.

## Benchmark/allocation test idea

Add an allocation test or BenchmarkDotNet comparison for `DotBoxdDotBoxdRpcMessagePackIpc.ListenNamedPipe`/`ConnectNamedPipeAsync` with default options versus a low-allocation options factory. Measure `AddAsync` and struct echo bytes/call, and fail if the documented default/profile regresses beyond an explicit allocation budget.
