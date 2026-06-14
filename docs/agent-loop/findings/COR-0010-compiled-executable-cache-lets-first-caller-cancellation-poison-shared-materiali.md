---
id: COR-0010
area: correctness
status: verified
priority: medium
title: Compiled executable cache lets first caller cancellation poison shared materialization
dedup_key: correctness/compiled-cache/shared-materialization/first-caller-cancellation-poisons-waiters
created_at: 2026-06-12T22:02:31.7262205+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T00:19:52.5062504+00:00
claimed_by: worker
claimed_at: 2026-06-13T00:13:47.2112159+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T00:15:01.6346277+00:00
fixed_commit: pending
verified_by: verifier
verified_at: 2026-06-13T00:19:52.5062504+00:00
verified_commit: 
duplicate_of: 
---

# COR-0010: Compiled executable cache lets first caller cancellation poison shared materialization

## Claim

`CompiledExecutableCache` shares the first caller's cancellation token with every concurrent waiter for the same compiled artifact. If that first token is cancelled while materialization is still running, other callers with live tokens can receive a cancelled/failed materialization even though their own execution was not cancelled.

## Evidence

`src/DotBoxd.Hosting/Execution/CompiledExecutableCache.cs` builds the cached lazy task as:

```csharp
var candidate = new Lazy<Task<MaterializedCompiledArtifact>>(
    () => _materialize(artifact, plan, entrypoint, cancellationToken).AsTask(),
    LazyThreadSafetyMode.ExecutionAndPublication);
```

The dictionary key is only `artifact.Manifest.CacheKey + "|" + artifact.AssemblyHash`, so a second concurrent `GetAsync` for the same artifact reuses the same `Lazy<Task<MaterializedCompiledArtifact>>`. That second caller waits with its own token via `lazy.Value.WaitAsync(cancellationToken)`, but the underlying materialization task was already created with the first caller's token. Cancelling the first caller can therefore cancel the shared materialization and make the second caller fail, even when the second caller's token remains valid.

The existing materialization tests cover reuse, hash validation, unload on dispose, and dispose during materialization, but they do not cover two concurrent callers with different cancellation tokens.

## Suggested test

Add a `CompiledExecutableCache` unit test with a custom materializer that records the passed token, blocks on a `TaskCompletionSource`, and is called through two concurrent `GetAsync` calls for the same artifact. Cancel only the first call's token before releasing the materializer. The second `GetAsync` should still complete successfully because its token was not cancelled. The current implementation can cancel the shared lazy task and fail both waiters.

## Expected behavior

A caller's cancellation should cancel that caller's wait, not the cache-wide materialization needed by other non-cancelled executions for the same artifact. Shared materialization should either use an internal/non-caller token or isolate cancellation so one waiter cannot poison the shared cache entry for other waiters.

## Deduplication key

`correctness/compiled-cache/shared-materialization/first-caller-cancellation-poisons-waiters`
