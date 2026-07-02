---
title: 'Examples'
description: 'The maintained, runnable example lives in the repository under samples/GameServer. It combines all three modes — service IPC, event kernels, live settings,…'
---
The maintained, runnable example lives in the repository under
[`samples/GameServer`](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer). It combines
all three modes — service IPC, event kernels, live settings, host bindings, policy-gated execution,
and a server extension (pushdown) — in one program.

One maintained program (instead of many small samples that drift out of sync) means every mode is
exercised end to end against the same host: services (RPC) for typed interop, the query/event pipeline
(RunLocal) for server-side filtering and projection, and pushdown for collapsing N calls into one
server-side batch. Regression tests pin that behavior in place — see the
[regression suite](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin.Tests/Regression/MonsterKillerServerExtensionRegressionTests.cs)
— so the walkthrough below describes code that is proven to run, not illustrative snippets.

- **[GameServer walkthrough](/examples/gameserver-walkthrough/)** — an annotated tour that maps each feature
  to the file that implements it.
- **[Coverage gaps](/examples/coverage-gaps/)** — features that used to live in removed samples and where they
  are exercised now.

Run it locally:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

Prefer a from-scratch build? Start with the [Tutorials](/tutorials/).
