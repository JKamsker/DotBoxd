# Pushdown

**Pushdown** turns many small remote calls into **one** validated server-side execution. The host is
typically **frozen at release** and exposes only **fine-grained** bindings — it ships no batch
operations. Instead of the client looping (one round-trip per entity), a **plugin ships its own
server-side batch aggregate** as a sandboxed **server extension**: the analyzer lowers a C# batch
method to verified IR that runs server-side, looping over the host's *existing* bindings. Only the
plugin changes; the server is never recompiled.

Without pushdown (host has only `Kill(id)`):

```text
client -> Kill(1)
client -> Kill(2)
...           (N round-trips)
```

With pushdown (plugin ships a `[ServerExtension]` batch aggregate):

```text
client -> KillMonsters([1..N])         (1 round-trip)
host   -> runs the plugin's verified kernel, looping ctx.Host<IGameWorld>().Kill(id) server-side
host   -> returns List<KillResult>     (one compact result)
```

The mechanism: mark a `partial class` `[ServerExtension("id", typeof(TService))]` with one public batch
method whose trailing `HookContext` parameter exposes host bindings (`ctx.Host<T>()` or an injected host
service field); the
[`DotBoxD.Plugins.Analyzer`](../../src/CodeGeneration/DotBoxD.Plugins.Analyzer) lowers it to verified IR
(supporting `foreach`, `if`/`else`, locals, host binding calls, DTO construction, and `List<T>`
accumulation — complex objects ride the IR `Record` type). The host installs it with
`server.RegisterServerExtensionAsync<TService, TKernel>()` and the caller invokes
`server.ServerExtension<TService>().Method(args)`. Over a process boundary, the
`DotBoxD.Pushdown.Services` MessagePack IPC addon forwards install + a compact binary IR invoke payload.

Because the batch logic is author-supplied, it runs as a validated sandboxed kernel: it reaches only the
host bindings the server already exposes, under the same capability + fuel/quota limits as event kernels.
A method reachable via normal RPC is **not** automatically reachable from a kernel.

**See also:** the runnable GameServer sample under
[`samples/GameServer`](../../samples/GameServer), which demonstrates the
`MonsterKillerKernel` server-extension path.
Roadmap items (`DotBoxD.Pushdown.Linq`, fluent client API) are tracked in
[follow-up-issues](../architecture/follow-up-issues.md).
