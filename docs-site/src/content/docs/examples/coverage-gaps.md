---
title: 'Example Coverage Gaps'
description: 'The only maintained runnable example is now:'
---
The only maintained runnable example is now:

```text
samples/GameServer
```

The previous example set is preserved at the Git tag `examples-before-prune-2026-06-15`. Use that tag
for historical reference only; the examples on that tag are not part of the maintained sample surface.

## Still Shown By GameServer

GameServer remains the prime example because it combines the core plugin model in one coherent domain:

- service IPC over the plugin control plane
- analyzer-generated event kernel packages
- package JSON export/import
- live setting updates over IPC
- event hook dispatch with server-owned adapters
- host bindings behind capability grants
- per-resource audit events from host bindings
- least-privilege policy construction
- compiled kernel execution
- kernel RPC pushdown for server-side batch work
- ordinary server APIs beside kernel execution
- plugin ownership tied to the IPC connection lifetime

## No Longer Shown In Maintained Examples

These capabilities are still part of the repository, but they are no longer demonstrated by a
maintained sample project after removing the older examples:

- A standalone Services-only client/server sample over TCP.
- Bidirectional service callbacks using two services on the same peer connection.
- The Inventory sample's generated async-sibling interface pattern for sync service methods.
- Nested service return values, where a root RPC returns a proxy bound to a server-side sub-service.
- A tiny all-in-one acceptance sample that shows Services, Kernels, and Pushdown without the richer
  GameServer domain.
- A standalone named-pipe plugin IPC server/client pair separate from GameServer.
- A minimal local in-process plugin install sample.
- Simple filter/formula contract guidance separate from package-backed event kernels.
- Topic-focused snippets for `BindValue<T>` and `BindContext<TSettings>`.
- A standalone HTTP transport binding sample.
- Small custom binding examples outside the GameServer world bindings.
- Standalone resource-limit demonstrations for loop, host-call, wall-time, list, and string quotas.
- Standalone audit-observer demonstrations for `SandboxHostBuilder.ForwardAuditEventsTo(...)`.
- Standalone package-manifest inspection examples for admin or marketplace UI surfaces.

Where these behaviors matter to correctness, keep or add focused tests under `tests/` rather than
reintroducing broad sample projects.
