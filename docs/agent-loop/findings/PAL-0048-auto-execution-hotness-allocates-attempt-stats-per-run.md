---
id: PAL-0048
area: perf_alloc
status: open
priority: medium
title: Auto execution hotness allocates attempt stats per run
dedup_key: alloc/hosting/auto-execution/hotness-attempt-stats-per-run
created_at: 2026-06-13T06:45:33.8326453+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-13T06:45:33.8326453+00:00
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

# PAL-0048: Auto execution hotness allocates attempt stats per run

Auto execution mode allocates a hotness attempt object and a full hotness stats snapshot for every run before dispatching interpreted or compiled execution.

## Evidence

- `src/DotBoxd.Hosting/Execution/SandboxHost.Auto.cs:21` calls `_autoHotness.BeginAttempt(plan, entrypoint)` for every `ExecutionMode.Auto` run after compiler/debug checks.
- `src/DotBoxd.Hosting/Internal/AutoExecutionHotness.cs:44` enters the per-plan hotness state and increments the run count on every attempt.
- `src/DotBoxd.Hosting/Internal/AutoExecutionHotness.cs:53` returns `new AutoHotnessAttempt(this, Snapshot())`, allocating the attempt wrapper and a `ModuleHotnessStats` record for every auto-mode invocation.
- `src/DotBoxd.Hosting/Internal/AutoExecutionHotness.cs:91` through `src/DotBoxd.Hosting/Internal/AutoExecutionHotness.cs:104` constructs the snapshot record from state fields even for the first-run interpreted path, where `SandboxHost.Auto.cs:22` only checks `hotness.Stats.RunCount == 1`.
- Existing `PAL-0030` covers unbounded retained `AutoHotnessState` entries by `planHash|entrypoint`. This finding is separate: even for one stable plan and bounded state, each auto-mode run creates per-attempt heap objects on the dispatch path.

## Impact

`ExecutionMode.Auto` is the default plugin server mode and can sit on high-frequency event handling. Once a plan is prepared and hotness state exists, the steady-state dispatch path should choose interpreted or compiled execution with minimal overhead. Instead, every auto run allocates at least the attempt wrapper plus hotness snapshot before doing sandbox work, and the first-run path allocates a full stats record just to observe `RunCount == 1`.

## Suggested fix direction

Make the hotness attempt a readonly struct or return the state plus primitive snapshot fields without an extra wrapper allocation. Consider exposing a non-allocating `BeginAttempt` result with `RunCount` and selector inputs, and only materialize `ModuleHotnessStats` when invoking a custom selector that needs the public record. Preserve locking and completion accounting semantics.

## Benchmark/allocation test idea

Add an auto-mode dispatch benchmark that executes one prepared pure entrypoint 1, 1,000, and 100,000 times with the default `HotnessExecutionModeSelector`, both before and after the compile threshold. Measure bytes allocated outside the interpreter/compiler execution body and assert warm auto-mode dispatch does not allocate an `AutoHotnessAttempt` or `ModuleHotnessStats` per run.
