---
title: 'Event pipelines'
description: 'An event pipeline lets a plugin react to a host event without receiving every event over the wire. You author one fluent chain —…'
---
An **event pipeline** lets a plugin react to a host event *without receiving every event over the wire*. You
author one fluent chain — `server.Hooks.On<TEvent>().Where(...).Select(...).<terminal>(...)` — and the
[`DotBoxD.Plugins.Analyzer`](https://github.com/JKamsker/DotBoxD/tree/main/src/CodeGeneration/DotBoxD.Plugins.Analyzer)
lowers the `Where` filter and `Select` projection to verified server-side [kernel IR](/concepts/kernels/). The host
does the matching and shaping first, so only the values you actually asked for cross the pipe — and only for
events that pass the filter. It is the event-push counterpart to [Pushdown](/concepts/pushdown/): both move author
logic next to the host's data instead of round-tripping.

This page is the model — the two registries, the pipeline stages, and every terminal ("run mode"). To build
one end to end against the maintained sample, follow the
[event-pipeline tutorial](/tutorials/event-pipeline-runlocal/).

## Why event pipelines? (and when to use it)

**The problem it beats.** The naive way to react to a server event is to subscribe to everything and filter
in the plugin. That ships every full record even when you discard most, wakes the plugin once per event, and
pays a process-boundary crossing for events you were never going to act on — expensive for high-volume event
families.

**The payoff.** Because `Where`/`Select` run server-side, only matching, projected values cross the pipe —
fewer bytes, fewer wake-ups, and no round-trips (the push is one-way, server → plugin). The split is
measured, not asserted: the sample's premise test publishes one matching and one missing event and proves
only the match crosses
([`RemoteRunLocalIpcPremiseTests.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin.Tests/RemoteRunLocalIpcPremiseTests.cs)).

- **Safe to accept from untrusted authors.** `Where`/`Select` are not plugin code running on the server —
  they lower to the same validated, fuel-metered, capability-gated [kernel IR](/concepts/kernels/) that event
  kernels run under; only a `*Local` terminal is trusted native C#, and it runs in *your* plugin process. A
  pure event-field chain needs no capability grant; when a chain reads gated host services the analyzer
  *derives* the required capabilities from the IR (the plugin can't self-grant), and install is fail-closed
  if the manifest asks for more than the host policy allows ([sandbox caveats](/security/sandbox-caveats/)).
- **Interest gets indexed for free.** When a `.Where` leaf compares an `[EventIndexKey]` property to a
  compile-time constant, the lowered chain ships index metadata so the host prefilters into equality/range
  buckets *before* entering the sandbox. You express interest declaratively; the framework — not the author —
  decides the wire cost.
- **No lock-in.** The fluent chain is opt-in sugar over public IR primitives — the lowered projection kernel
  plus its manifest are verified JSON IR you could hand-author and run through the same `SandboxHost`
  pipeline.

**When to use it:** you react to a server event stream but only need a filtered/shaped subset locally, or a
decision returned to the host. **When to prefer another mode:** for a chatty request/response loop you want
to collapse into one aggregating server-side batch, use [Pushdown](/concepts/pushdown/); for a single
request→response to a host capability, use a [Service](/concepts/services/).

## Two registries: `server.Hooks` vs `server.Subscriptions`

A plugin attaches every pipeline to one of two registries, and the choice sets the semantics:

| Registry | It models | Handlers run | Can change the outcome? | Result terminals? |
|---|---|---|---|---|
| `server.Hooks` | awaited **decision points** | sequentially, awaited to completion; exceptions propagate | yes | yes (`Register` / `RegisterLocal`) |
| `server.Subscriptions` | fire-and-forget **notifications** | isolated background delivery; a fault is contained, not propagated | no | no |

Reach for **Hooks** when the host is asking *"what should happen?"* and your reaction can change the answer
(scale a damage number, allow or deny an action). Reach for **Subscriptions** when you only need to *know* an
event happened and nothing waits on you. The stages and terminals below work on both — except the result
terminals, which exist only on Hooks.

## The stages

Every pipeline is the same shape; only `On` and the terminal are required:

1. **`.On<TEvent>()`** — choose the event type to react to.
2. **`.Where(predicate)`** — a **server-side filter**, lowered to verified IR; it runs on the host, so events
   that don't match never cross the pipe. A `[EventIndexKey]` constant comparison additionally prefilters
   through a host index before the sandbox runs.
3. **`.Select(projection)`** — a **server-side projection**, lowered to IR, so only the projected value —
   not the whole event record — crosses. Omit it to hand the terminal the whole event.
4. **a terminal** — what to do on a match (next section).

Every stage offers an element-only (`x => ...`) and an `(element, context)` overload, chosen independently.
`Where`/`Select` always run in the sandbox; only the terminal can be native plugin code — so what crosses the
pipe is exactly *the projected value of each matching event*, and nothing for events that miss.

## The terminals (run modes)

The terminal is the last call — what happens when an event matches. Pick one on two axes: **where your
handler runs** (in your plugin as native C#, or server-side as sandboxed IR) and **whether it returns a
decision** the host acts on.

| | Handler runs **in your plugin** (native C#) | Handler runs **server-side** (sandboxed IR) |
|---|---|---|
| **Fire-and-forget** (react, return nothing) | **`RunLocal`** | **`Run`** |
| **Return a decision** (`IHookResult`) | **`RegisterLocal`** | **`Register`** |

| Terminal | Handler runs… | What crosses the pipe | Returns a value? | Valid on |
|---|---|---|---|---|
| `RunLocal` | your plugin (native C#) | the projected value only, one-way push | no | Hooks + Subscriptions |
| `Run` | server-side (lowered to IR) | nothing — runs on the host | no | Hooks + Subscriptions |
| `RegisterLocal` | your plugin (native C#) | projected value out, `IHookResult` back (round-trip) | yes | Hooks only |
| `Register` | server-side (lowered to IR) | nothing — decided on the host | yes | Hooks only |
| `Use<TKernel>` | server-side, as an installed kernel | nothing | as the kernel defines | Hooks + Subscriptions |

- **`RunLocal`** — react in native plugin C#. Use it when the reaction touches your own plugin state or
  services (emit a message, update local UI). It is a one-way push; there is no reverse channel to mutate
  server state — do that as a separate server call keyed by id.
- **`Run`** — the same idea, but lowered to run entirely on the host. Use it when the effect is purely
  host-side (call a host binding) and nothing needs to reach the plugin.
- **`RegisterLocal`** — a **result hook**: the filter runs server-side, the matching event is pushed to your
  plugin, your delegate computes an `IHookResult`, and that value returns to the host to influence the
  decision. Use it when computing the decision needs your plugin's own types or services. Carries an
  `int priority`; the result type is a `readonly record struct … : IHookResult`.
- **`Register`** — the server-side counterpart to `RegisterLocal`: the whole decision (filter, projection,
  and result computation) lowers into the sandbox and runs on the host, with nothing crossing the pipe. Use
  it when the decision is pure host-data logic.
- **`Use<TKernel>()`** — install a separately-authored [kernel](/concepts/kernels/) as the reaction, wired at setup
  time. Use it when the whole reaction is itself validated sandbox logic.

Two rules follow from the tables. **Result terminals (`Register`, `RegisterLocal`) exist only on
`server.Hooks`** — a fire-and-forget subscription has no decision to return to. And **`Run`, `Register`, and
`RegisterLocal` are lowered by the analyzer at build time**, so you author them in a plugin project that
references the `DotBoxD.Plugins.Analyzer` (a raw, un-lowered call throws `DBXK062`); with `RunLocal`, only
the `Where`/`Select` lower and the terminal delegate stays native.

## See also

- [Event-pipeline tutorial](/tutorials/event-pipeline-runlocal/) — build a pipeline end to end against
  the maintained sample, with the runnable chains from
  [`LocalReactions.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Authoring/LocalReactions.cs).
- [Kernels](/concepts/kernels/) — the validated, fuel-metered sandbox the `Where`/`Select` IR runs inside, and what
  `Use<TKernel>` installs.
- [Pushdown](/concepts/pushdown/) — the sibling server-side mode: aggregate N round-trips into one instead of
  reacting to an event stream.
- [Sandbox caveats](/security/sandbox-caveats/) — the trust boundary before you ship a `RunLocal` delegate
  that runs native plugin code.
- [GameServer walkthrough](/examples/gameserver-walkthrough/) — the whole sample end to end: services,
  kernels, pushdown, and event pipelines together.
