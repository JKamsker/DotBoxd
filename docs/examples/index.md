# Examples

The maintained, runnable example lives in the repository under
[`samples/GameServer`](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer). It combines
all three modes — service IPC, event kernels, live settings, host bindings, policy-gated execution,
and a server extension (pushdown) — in one program.

- **[GameServer walkthrough](gameserver-walkthrough.md)** — an annotated tour that maps each feature
  to the file that implements it.
- **[Coverage gaps](coverage-gaps.md)** — features that used to live in removed samples and where they
  are exercised now.

Run it locally:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

Prefer a from-scratch build? Start with the [Tutorials](../tutorials/index.md).
