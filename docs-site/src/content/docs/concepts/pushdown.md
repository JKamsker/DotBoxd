---
title: 'Pushdown'
description: 'Pushdown turns many small remote calls into one validated server-side execution. The host is typically frozen at release and exposes only fine-grained…'
---
**Pushdown** turns many small remote calls into **one** validated server-side execution. The host is
typically **frozen at release** and exposes only **fine-grained** bindings — it ships no batch
operations. Instead of the client looping (one round-trip per entity), a **plugin ships its own
server-side batch aggregate** as a sandboxed **server extension**: the analyzer lowers a C# batch
method to verified IR that runs server-side, looping over the host's *existing* bindings. Only the
plugin changes; the server is never recompiled.

## Why Pushdown? (and when to use it)

**The problem it beats.** A shipped host is usually frozen at release and exposes only the *safe*
fine-grained primitives it wants to hand the sandbox (`Kill(id)` — one monster). A client acting on many
entities is then forced into a chatty loop where every iteration is a network hop: for N monsters that is
N remote calls, N serialization passes, and N chances for latency to dominate. You cannot add
`KillMonsters(...)` to the host (it is already released), and a bespoke trusted per-plugin RPC endpoint is
impossible because the server cannot be recompiled.

**The payoff.** Pushdown collapses those N round-trips into **one**. The plugin ships a `[ServerExtension]`
batch that runs *next to the host's data*, looping the host's existing bindings in-process — local calls,
no IPC hop per entity — and returns one compact result. The GameServer `MonsterKillerKernel` does five
reads/writes per entity server-side yet returns only the final `List<MonsterKillResult>`; client-side that
would be ~5N round-trips.

- **Host stays frozen and minimal.** New coarse operations arrive by installing a plugin, never by shipping
  a new host build; the untrusted batch logic lives in the plugin, keeping the trusted host surface small.
- **Composition the host never anticipated.** The batch recombines primitives the host already exposes — the
  same reads/writes back `MonsterKillerKernel` (batch over a list), `RangeMonsterKillerKernel` (spatial
  predicate + result cap), and `BlinkKernel` (per-instance graft).
- **Untrusted-author code under a real sandbox.** The batch is plain C#, but the analyzer lowers it to the
  same validated, capability-gated, fuel-metered [kernel IR](/concepts/kernels/) that event kernels run under (see
  [sandbox caveats](/security/sandbox-caveats/)) — not a trusted plugin with CLR access. It reaches only
  registered bindings, and install fails (the code never runs) if the manifest requests a capability the host
  policy did not grant.
- **No lock-in.** `[ServerExtension]` is opt-in sugar over public IR primitives — everything it emits is
  verified JSON IR plus a manifest you could hand-author and run through the same `SandboxHost` pipeline.

**When to use it:** a client would otherwise loop with one round-trip per entity against a frozen host and
the batch fits the lowering surface (`foreach`/`if`/locals/host-calls/DTO/`List<T>`).
**When to prefer another mode:** for a one-way push of a filtered/shaped *event stream* with no return value,
use the [event pipeline (RunLocal)](/tutorials/event-pipeline-runlocal/); for a single request/response
call to a host capability, use a [Service](/concepts/services/); if you control the host and it is not frozen, add
the method to the host directly; if the body needs arbitrary CLR calls or streaming results, Pushdown's
one-shot sandboxed model does not apply.

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
[`DotBoxD.Plugins.Analyzer`](https://github.com/JKamsker/DotBoxD/tree/main/src/CodeGeneration/DotBoxD.Plugins.Analyzer) lowers it to verified IR
(supporting `foreach`, `if`/`else`, locals, host binding calls, DTO construction, and `List<T>`
accumulation — complex objects ride the IR `Record` type). The host installs it with
`server.RegisterServerExtensionAsync<TService, TKernel>()` and the caller invokes
`server.ServerExtension<TService>().Method(args)`. Over a process boundary, the
`DotBoxD.Pushdown.Services` MessagePack IPC addon forwards install + a compact binary IR invoke payload.

Because the batch logic is author-supplied, it runs as a validated sandboxed kernel: it reaches only the
host bindings the server already exposes, under the same capability + fuel/quota limits as event kernels.
A method reachable via normal RPC is **not** automatically reachable from a kernel.

**See also:** for a guided next stop, the beginner-friendly
[GameServer walkthrough](/examples/gameserver-walkthrough/) narrates this pushdown path end to end;
the runnable sample it is based on lives under
[`samples/GameServer`](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer), which demonstrates the
`MonsterKillerKernel` server-extension path.
Roadmap items (`DotBoxD.Pushdown.Linq`, fluent client API) are tracked in
[follow-up-issues](https://github.com/JKamsker/DotBoxD/blob/main/docs/architecture/follow-up-issues.md).
