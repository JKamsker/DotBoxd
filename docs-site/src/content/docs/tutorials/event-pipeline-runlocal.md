---
title: 'Tutorial: event pipelines — filter server-side, react where it fits'
description: 'This tutorial builds an event pipeline end to end — DotBoxD''s fluent way to react to a server-side event without streaming every event over the wire. You…'
---
This tutorial builds an **event pipeline** end to end — DotBoxD's fluent way to react to a server-side event *without streaming every event over the wire*. You author one chain — `server.Hooks.On<TEvent>().Where(...).Select(...).<terminal>(...)` — and the analyzer splits it across the process boundary for you: the `Where` **filter** and `Select` **projection** lower to verified IR that **runs on the server**, and only the small projected value crosses the IPC pipe — only for events that pass the filter.

The last stage — the **terminal** — decides *where your reaction runs and whether it hands a value back*. This walkthrough builds with **`RunLocal`** (react in native plugin C#), then [Step 6](#step-6--choosing-a-terminal) lays out all five terminals — `RunLocal`, `Run`, `RegisterLocal`, `Register`, and `Use<TKernel>` — side by side. For the whole model in one place — the two registries, the stages, and every terminal — see the [Event pipelines concept](/concepts/event-pipelines/).

The payoff: a plugin subscribing to a high-frequency event does not have to receive every event and filter it locally. The server does the filtering and shaping first, so the pipe carries only the few small values you actually asked for — fewer bytes, fewer wake-ups, and no round-trips (the push is one-way, server → plugin). This is the same "collapse remote traffic into server-side work" idea as [Pushdown](/tutorials/pushdown-server-extension/), applied to the event-push direction.

Everything below uses the real, compiling API. The runnable chains live in the GameServer sample's [`LocalReactions.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Authoring/LocalReactions.cs); the 2-process premise is proven end to end over a live named pipe in [`RemoteRunLocalIpcPremiseTests.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin.Tests/RemoteRunLocalIpcPremiseTests.cs).

## Why event pipelines? (and when to use it)

**The problem it solves.** The naive way to react to a server event is to subscribe to everything and filter in the plugin. That serializes and ships every full record even when you discard most, wakes the plugin per event, and pays a process-boundary crossing for events you were never going to act on. The design doc names this cost directly: "broad subscription + run the lowered predicate for every event — correct, but expensive for high-volume event families" ([`index-predicate-metadata.md`](https://github.com/JKamsker/DotBoxD/blob/main/docs/design/plugin-fluent-hooks-api/index-predicate-metadata.md)).

**The payoff — measured, not asserted.** Because `Where`/`Select` run server-side, only matching, projected values cross the pipe. A live-pipe premise test proves it by publishing two events (one match, one miss) and asserting the split directly — Step 3 walks the three assertions against [`RemoteRunLocalIpcPremiseTests.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin.Tests/RemoteRunLocalIpcPremiseTests.cs).

**It's safe to accept from untrusted authors.** `Where`/`Select` are not plugin code running on the server — they lower to the same validated, fuel-metered, capability-gated sandbox IR that [event kernels](/concepts/kernels/) run under; only `RunLocal` is trusted native C#, and it runs in *your* plugin process. A pure event-field chain needs no capability grant (`subscription.LocalTerminal == true`, empty `RequiredCapabilities`); when a chain reads gated host services the analyzer *derives* the required capabilities from the IR — the plugin can't self-grant authority — and install is fail-closed if the manifest asks for more than the policy allows ([`capability-gating.md`](https://github.com/JKamsker/DotBoxD/blob/main/docs/design/plugin-fluent-hooks-api/capability-gating.md)).

**Constant-predicate interest gets indexed for free.** When a `.Where` leaf compares an `[EventIndexKey]` property to a compile-time constant, the lowered chain ships index metadata so the host prefilters into equality/range buckets *before* entering the sandbox; the verified IR still runs as the correctness fallback for whatever the index lets through ([`index-predicate-metadata.md`](https://github.com/JKamsker/DotBoxD/blob/main/docs/design/plugin-fluent-hooks-api/index-predicate-metadata.md)). You express interest declaratively; the framework — not the author — decides the wire cost.

**When to use it — and when not.** Reach for `RunLocal` when you react to an event stream but only need a subset or summary locally. Prefer **`.Run`** when the effect is purely host-side (nothing needs to cross the pipe at all). Prefer **`.RegisterLocal`** when you need a decision value returned to the server. Prefer **[Pushdown](/tutorials/pushdown-server-extension/)** when the shape is a chatty request/response loop you want to collapse into one aggregating server-side batch — RunLocal is one-way push (server → plugin), Pushdown aggregates and returns. And don't try to mutate server state from `RunLocal`: there is no reverse channel by design — do side effects as a separate server call keyed by id.

## What you'll build

- A **plugin process** that builds the generated `IGameWorldServer` facade from a pipe name and starts it.
- A **filter pipeline** — `On<MonsterAggroEvent>().Where(...).Select(...).RunLocal(...)` — where the filter and projection run server-side and only the projected `MonsterId` reaches your delegate.
- Three variants of the same shape: a **scalar projection**, the **whole-event** form (no `Select`), and the **server-context** form (`(x, ctx) => ...`).
- A side-by-side map of all five terminals — `.RunLocal`, `.Run`, `.RegisterLocal`, `.Register`, and `.Use<TKernel>()` — so you can pick the right one (full reference: [Event pipelines](/concepts/event-pipelines/#the-terminals-run-modes)).

## Prerequisites

> **Follow along against the sample; don't paste these snippets into an empty project.** Unlike the [first Service tutorial](/tutorials/first-service/), every type on this page — `GamePluginServer`, `GamePluginContext`, `IGameWorldServer`, `MonsterAggroEvent`, and the rest — is defined by the maintained GameServer sample, not the framework. Clone [the repo](https://github.com/JKamsker/DotBoxD) and read and run the sample as you follow along; Step 7 lists the exact files and the command to launch it.

You don't need to install anything to run the sample — the repo already references these packages. Install this set only if you scaffold your own plugin project instead. Event pipelines live on the same net10.0 Plugins stack as Pushdown. Add the authoring contracts, the host runtime, the generator/analyzer, and the IPC addon (the plugin and host are separate processes here):

```bash
# Plugin authoring contracts: [GeneratePluginServer], HookContext, event/hook attributes
dotnet add package DotBoxD.Abstractions --prerelease

# Host runtime that loads, validates, and dispatches plugins (the RemoteHook* fluent runtime)
dotnet add package DotBoxD.Plugins --prerelease

# Source generator + analyzer that LOWERS .Where/.Select/.RunLocal to verified IR
dotnet add package DotBoxD.Plugins.Analyzer --prerelease

# (Cross-process) MessagePack IPC addon that connects the plugin to the host over a named pipe
dotnet add package DotBoxD.Pushdown.Services --prerelease
```

Package names and purposes are from the README "Installing from NuGet" table ([`README.md`](https://github.com/JKamsker/DotBoxD/blob/main/README.md)).

> **The analyzer is load-bearing, not optional.** For the remote hook family, `.Where`/`.Select`/`.RunLocal` are **lowering markers**: the `DotBoxD.Plugins.Analyzer` intercepts the call sites and replaces them with a lowered projection kernel plus a native-delegate registration. Without interception the library throws at runtime (`RunLocal` throws "requires an event callback transport"; `Run` throws "must be intercepted by the DotBoxD plugin generator"). That is why the chains must be authored **in the plugin project** — the project the analyzer runs on. Because interception happens at compile time, an unsupported shape inside a lowered `.Where`/`.Select` — a construct the sandbox IR can't express — surfaces as a build-time diagnostic from the same `DotBoxD.Plugins.Analyzer` (kernel diagnostics use the `DBXK` prefix), not as a runtime surprise. See [`RemoteHookPipeline.Typed.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Hosting/DotBoxD.Plugins/Runtime/Hooks/Remote/RemoteHookPipeline.Typed.cs).

## Step 1 — Build and start the generated server

A plugin marks one partial class with `[GeneratePluginServer]` and the analyzer emits the whole facade around it — the RPC proxy, the `GamePluginServerBuilder`, the `IGameWorldServer` lifecycle type, and the `Hooks` / `Subscriptions` registries. The one-liner opt-in ([`GamePluginServer.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/GamePluginServer.cs)):

```csharp
[GeneratePluginServer(Context = typeof(GamePluginContext))]
public partial class GamePluginServer : IGameWorldAccess;
```

The plugin's `Main` resolves a pipe name, then builds and starts the generated server ([`Program.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Program.cs)):

```csharp
var pipeName = GamePluginServerHost.PipeNameFromArgs(args); // the host printed this pipe name

using IGameWorldServer server = GamePluginServerBuilder
    .FromPipeName(pipeName)
    .Setup(s =>
    {
        // Setup-time hooks are RECORDED here and replayed at StartAsync (see Step 6).
        s.Hooks.On<MonsterAggroEvent>().Use<GuardianKernel>();
    })
    .Build();                  // Build() is sync and does no I/O.

await server.StartAsync();     // StartAsync() connects the pipe and ships the recorded IR.
```

`Build()` is synchronous and performs no I/O; `FromPipeName` defers the actual pipe connection until `StartAsync()`, which is where the recorded IR is shipped to the host. Pipe-name resolution and the RPC transport itself are covered in the [first Service tutorial](/tutorials/first-service/) — here we focus on what you do with `server.Hooks` once the server is up. The server exposes two event registries: `server.Hooks` are awaited decision points whose logic can influence the outcome, while `server.Subscriptions` (Step 6) are fire-and-forget notifications.

## Step 2 — Install a filter pipeline

After `StartAsync()`, subscribe to a server event and author the reaction. This is the canonical
filter pipeline — `On<T>().Where().Select().RunLocal()`
([`LocalReactions.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Authoring/LocalReactions.cs)):

```csharp
using DotBoxD.Kernels.Game.Server.Abstractions.Events;

var calmedMonsters = new List<string>();

server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 4)                    // lowered -> runs on the SERVER as verified IR
    .Select(e => e.MonsterId)                       // lowered -> runs on the SERVER; projects one field
    .RunLocal(monsterId => calmedMonsters.Add(monsterId)); // native C#, runs in YOUR plugin process
```

The event is an ordinary positional record ([`MonsterAggroEvent.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server.Abstractions/Events/MonsterAggroEvent.cs)), in namespace `DotBoxD.Kernels.Game.Server.Abstractions.Events`:

```csharp
public sealed record MonsterAggroEvent(
    string MonsterId, string PlayerId, int Distance, int MonsterLevel, int PlayerLevel);
```

There is no hand-written adapter — the framework infers the sandbox shape from the record's properties. Three things run in three different places:

- **`Where(e => e.Distance <= 4)`** — the filter. Lowered to server-side verified IR; it runs on the server *before* any byte hits the wire.
- **`Select(e => e.MonsterId)`** — the projection. Also lowered to server-side IR; it reduces the 5-field record to the single `string` you asked for.
- **`RunLocal(monsterId => ...)`** — the terminal. This is the *only* part that does not lower. It is trusted native plugin C#, invoked in your plugin process once the projected value arrives over the pipe.

## Step 3 — What actually crosses the pipe (the payoff)

The whole point is the 2-process split, and the sample has an end-to-end test that proves it on a **real** named pipe. It publishes two `MonsterAggroEvent`s server-side — one that matches the filter, one that does not ([`RemoteRunLocalIpcPremiseTests.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin.Tests/RemoteRunLocalIpcPremiseTests.cs)):

```csharp
await server.Hooks.PublishAsync(new MonsterAggroEvent("monster-7", "player-1", 3,  8, 1)); // Distance 3  -> matches
await server.Hooks.PublishAsync(new MonsterAggroEvent("monster-9", "player-2", 10, 8, 1)); // Distance 10 -> filtered
```

and then asserts the premise directly:

- **The filter ran server-side, before any IPC.** Of the two events published, the delivery count over the pipe is exactly **one** (`callbackSink.PushCount == 1`). The non-matching event produced **zero** wire traffic — it never reached the pipe. If filtering had leaked to the plugin side, the count would be 2.
- **Only the projection crossed — not the whole event.** The plugin-side list receives exactly `["monster-7"]`: the projected `MonsterId` scalar, not the 5-field record. The other fields (`PlayerId`, `Distance`, `MonsterLevel`, `PlayerLevel`) are simply not available client-side, because they never crossed. What physically travels the pipe is a small encoded byte payload of that one value.
- **A projection terminal does no server-side host send.** The server's message sink stays empty — a `RunLocal` terminal produces its effect in the *plugin* process, not on the host. (Contrast `.Run(...)` in Step 6, which *does* send on the host.)

The lowered chain installs as a real subscription that is marked a local terminal and requires **no capability** — because the `Where`/`Select` only read event fields and touch no capability-gated host binding, they lower to IR that needs no grant. Everything before `RunLocal` runs as the same validated, fuel-metered sandbox IR that event kernels run under; only the `RunLocal` delegate is your trusted, non-sandboxed code. That trust split is the platform boundary described in [`README.md`](https://github.com/JKamsker/DotBoxD/blob/main/README.md) and [Sandbox caveats](/security/sandbox-caveats/).

So the concrete win is measurable: **fewer bytes** (one scalar string instead of a five-field record), **fewer wake-ups** (one push instead of two; zero for filtered events), and **no round-trips** (the push is one-way server → plugin).

## Step 4 — Whole-event RunLocal (no `Select`)

Sometimes you want the full record, not a projection. Drop the `Select` and the `Where` filter still lowers to server-side IR — but now, for each matching event, the **whole event record** crosses the pipe and your delegate runs with the full event ([`LocalReactions.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Authoring/LocalReactions.cs)):

```csharp
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 4)          // still lowered -> filter runs on the server
    .RunLocal(aggro => onAggro(aggro));   // no Select — the FULL MonsterAggroEvent crosses per match
```

The premise test covers this variant too: filtering is still server-side (one push of the two published events), but the lowered subscription's projected type is the event type itself, and the plugin side receives the complete record with every field equal. Reach for this when the delegate genuinely needs several fields; prefer a `Select` projection (Step 2) when it needs only one, to keep the wire payload minimal.

## Step 5 — RunLocal with server context

Every fluent stage offers both an element-only and an `(element, context)` overload, chosen independently. The `(x, context)` form of `RunLocal` hands your delegate the plugin-side context alongside the projected value ([`LocalReactions.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Authoring/LocalReactions.cs)):

```csharp
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 4)
    .Select(e => e.MonsterId)
    .RunLocal((monsterId, context) =>
        onCalmedMonster(context.FormatCalmTarget(monsterId), context.HasCancelableDispatch));
```

Here `context` is the plugin's `GamePluginContext` — the type you pinned with `[GeneratePluginServer(Context = typeof(GamePluginContext))]`. It is a `partial class` that mixes hand-authored members with generator-emitted ones ([`GamePluginContext.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/GamePluginContext.cs)):

- Hand-authored helpers such as `FormatCalmTarget(string monsterId) => "ctx:" + monsterId`.
- Generator-emitted members such as `HasCancelableDispatch`, `Messages`, and `CancellationToken`.

This delegate still runs in the plugin process — the `context` is your client-side context, distinct from the host-service selector used inside `Where`/`Select` on the server. It is the seam for local side effects: emitting messages, reading the cancellation token, or calling into your own plugin services.

## Step 6 — Choosing a terminal

`RunLocal` is one of several **terminals** — the last call in the chain, which says what to *do* when an
event matches. The others are `Run`, `RegisterLocal`, `Register`, and `Use<TKernel>`. They all sit on the
same `.On<T>().Where(...).Select(...)` shape, so you pick one by answering just two questions:

1. **Where should your reaction run** — in *your plugin* as native C#, or *server-side* as sandboxed IR?
2. **Does it need to return a decision** the server acts on, or is it fire-and-forget?

Those two axes give a 2×2 (plus `Use<TKernel>` for a reaction you authored as a separate kernel):

| | Reaction runs **in your plugin** (native C#) | Reaction runs **server-side** (sandboxed IR) |
|---|---|---|
| **Fire-and-forget** (react, return nothing) | **`RunLocal`** | **`Run`** |
| **Return a decision** (`IHookResult`) | **`RegisterLocal`** | **`Register`** |

The same five terminals as a lookup:

| Terminal | Your handler runs… | What crosses the pipe | Returns a value? | Valid on |
|---|---|---|---|---|
| `RunLocal` | in your plugin (native C#) | the projected value only, one-way push | no | Hooks + Subscriptions |
| `Run` | server-side (lowered to IR) | nothing — runs on the host | no | Hooks + Subscriptions |
| `RegisterLocal` | in your plugin (native C#) | projected value out, `IHookResult` back (round-trip) | yes | Hooks only |
| `Register` | server-side (lowered to IR) | nothing — decided on the host | yes | Hooks only |
| `Use<TKernel>` | server-side, as an installed kernel | nothing | as the kernel defines | Hooks + Subscriptions |

> **Two rules fall out of that table.** *Result terminals* (`Register`, `RegisterLocal`) exist only on
> **`server.Hooks`** — the awaited decision points — because only a decision consumes a return value;
> **`server.Subscriptions`** are fire-and-forget, so they take just `RunLocal` / `Run` / `Use`. And `Run`,
> `Register`, and `RegisterLocal` are **lowered by the analyzer at build time**, so you author them in a
> plugin project that references the DotBoxD analyzer (a raw, un-lowered call throws `DBXK062`). With
> `RunLocal`, only the `Where` / `Select` lower — your terminal delegate stays native plugin code.

The rest of this step shows each server-side and result terminal against the `RunLocal` baseline from
Steps 1–5. For these same terminals framed as part of the whole model — alongside the two registries and
the stages — see the [Event pipelines concept](/concepts/event-pipelines/#the-terminals-run-modes).

**`.Run((x, ctx) => ...)` — the terminal stays on the server.** Swap `RunLocal` for `Run` and the filter, the projection, *and* the terminal all lower to verified IR and run fully server-side. There is no plugin-process delegate at all; the send happens on the host ([`Program.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Program.cs)):

```csharp
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 4)
    .Select(e => e.MonsterId)
    .Run((monsterId, ctx) => ctx.Messages.Send(monsterId, "calm:inline")); // lowered -> runs on the SERVER
```

Use `.Run` when the reaction is a host-side effect (send a message, touch a host binding) and nothing needs to cross to the plugin. Use `.RunLocal` when the reaction is native plugin code.

**`.RegisterLocal(...)` — a result hook (request/response).** This is the one shape where a value flows plugin → server. The filter runs server-side; on a match the event is pushed to the plugin, your delegate computes an `IHookResult`, and that result is returned to the server over the pipe. Result terminals carry a `priority` and a struct result type ([`LocalReactions.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Authoring/LocalReactions.cs)):

```csharp
server.Hooks.On<RemoteDamageDecisionEvent>()
    .Where(e => e.Damage > 10)
    .RegisterLocal(
        (e, context) => new RemoteDamageDecisionResult(
            true, context.DamageDecisionReason, context.ScaleDamageDecision(e.Damage)),
        priority: 7);
```

The event and its result are declared together ([`RemoteDamageDecisionEvent.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server.Abstractions/Events/RemoteDamageDecisionEvent.cs)); the result type is a `readonly partial record struct ... : IHookResult` (all `RegisterLocal` overloads constrain `TResult : struct, IHookResult`). The premise test proves the filter is still server-side: a `Damage == 5` event misses (the result path fires zero times), while `Damage == 12` hits and returns `Success` with the plugin-computed values back over the pipe.

**`.Register(...)` — return a decision, but decide on the server.** `Register` is the server-side
counterpart to `RegisterLocal`: the filter, the projection, *and* the `IHookResult` computation all lower
into the sandbox and run on the host, so no event is pushed to the plugin and no value travels back over the
pipe. Reach for it when the decision is pure host-data logic; choose `RegisterLocal` when computing the
decision needs your plugin's own types or services. The shape is otherwise identical — an `int priority` and
a `readonly record struct … : IHookResult` result — and, like every result terminal, it lives only on
`server.Hooks`.

**`.Use<TKernel>()` — record a generated kernel at setup time.** Instead of an inline lambda, `Use` resolves a generated kernel package and wires it as a decision hook. It is authored inside `Setup(...)` (Step 1) so `StartAsync()` can ship and install the kernel ([`Program.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Program.cs)):

```csharp
s.Hooks.On<MonsterAggroEvent>().Use<GuardianKernel>();          // awaited decision kernel
s.Subscriptions.On<AttackEvent>().Use<RetaliationKernel>();     // fire-and-forget notification kernel
```

Reach for `.Use<TKernel>()` when the whole reaction is itself validated sandbox logic you want to author as a kernel — see [Kernels](/concepts/kernels/) and [Pushdown](/tutorials/pushdown-server-extension/).

## Step 7 — The maintained runnable example

The chains on this page are lifted verbatim from the maintained GameServer sample, and the 2-process behavior is asserted by its tests. Rather than rebuild a plugin and host from scratch, run the sample and read the real authoring:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

What you should see: the sample prints three phases — a **baseline** run with no plugin, a **with-plugins** run where the plugin's lowered reactions take effect, and a **summary** confirming the plugin's kernels unloaded on disconnect. For the annotated console output, see [What the run prints](/examples/gameserver-walkthrough/#what-the-run-prints).

- Canonical authoring (all four shapes: scalar projection, `(x, context)`, whole-event, and `RegisterLocal`): [`LocalReactions.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Authoring/LocalReactions.cs).
- End-to-end 2-process premise proof over a live named pipe: [`RemoteRunLocalIpcPremiseTests.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin.Tests/RemoteRunLocalIpcPremiseTests.cs).
- An in-process preview of the same `RunLocal` shape (no pipe, for local experimentation): [`AdvancedUsage.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/AdvancedUsage.cs).
- The full facade wiring — setup-time `Use`, runtime `.Run` chains, and indexed subscriptions: [`Program.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Program.cs).

## Next steps

- [Event pipelines concept](/concepts/event-pipelines/) — the whole model in one place: the two registries (`Hooks` vs `Subscriptions`), the `On`/`Where`/`Select` stages, and all five terminals with when-to-use-each.
- [Pushdown — ship a server-side batch operation](/tutorials/pushdown-server-extension/) — the sibling tutorial: the *other* way author logic runs server-side, aggregating N round-trips into one instead of reacting to events.
- [Kernels concept](/concepts/kernels/) — the validated, fuel-metered sandbox the `Where`/`Select` IR runs inside, and what `.Use<TKernel>()` installs.
- [Services concepts](/concepts/services/) — the RPC dispatch model, peers/hosts, and the named-pipe transport that carries the projected values.
- [Sandbox caveats](/security/sandbox-caveats/) — what is and isn't a trust boundary before you ship a `RunLocal` delegate that runs native plugin code.
- [Diagnostics reference](/reference/diagnostics/) — the `DBXK` build-time diagnostics the analyzer raises when a `.Where`/`.Select` shape can't be lowered.
- [GameServer walkthrough](/examples/gameserver-walkthrough/) — the whole sample end to end: services, kernels, pushdown, and event pipelines together.
