---
title: 'Services (RPC)'
description: 'A Service is a handwritten host capability behind a shared C# contract. Annotate an interface with'
---
A **Service** is a handwritten host capability behind a shared C# contract. Annotate an interface with
`[DotBoxDService]` and the `DotBoxD.Services.SourceGenerator` emits, at compile time:

- a typed **client proxy** (calls marshal over the wire, no runtime reflection),
- a server **dispatcher**, and
- `Provide{Service}` / `Get<TService>()` wiring extensions.

The runtime is **peer-based and bidirectional** (`RpcPeer` / `RpcHost`): one connection can both serve
and call services. It is transport- and codec-neutral:

- **Transports**: `DotBoxD.Transports.Tcp`, `DotBoxD.Transports.NamedPipes` (and an in-process channel
  for tests). Channels carry framed messages and know nothing about services.
- **Codecs**: `DotBoxD.Codecs.MessagePack`.

The Services + channel + codec libraries target **netstandard2.1**, so they run on **Unity / IL2CPP**.

## Why Services (RPC)?

**The problem it solves: interop without hand-written marshaling.** Classic RPC makes you build a
request envelope, serialize args, match each response back to its call, deserialize, and cast —
repetitive and easy to get subtly wrong. Schema-first or hand-rolled stubs have a worse failure mode:
the client stub and the server handler drift apart (a renamed param, a changed return type) and you
only find out at runtime.

**The payoff: one C# interface is the single source of truth.** Both the proxy and the dispatcher are
generated from that one interface, so they cannot drift — a contract-shape mismatch is a compile error
(`DBXS###`), not a wire fault. The implementation is just your logic: nothing DotBoxD-specific leaks
into `class CatalogService : ICatalogService`; the client calls
`connection.Get<ICatalogService>().GetUnitPriceAsync("sword")` and the generated proxy does all the
marshaling — one method, one remote round-trip.

Grounded aspects:

- **AOT / Unity / IL2CPP reach.** There is no runtime reflection on the hot path and no assembly scan
  — proxy/dispatcher lookup goes through a *generated* registry — so the netstandard2.1 Services stack
  runs where IL2CPP and NativeAOT (ahead-of-time compilation) forbid dynamic reflection. The MessagePack codec is reflection-free
  too (generated formatters).
- **Peer-based, bidirectional.** Direction is configuration, not type: the same connection can both
  `Provide` and `Get`, so the host can call back into a connected plugin over one demuxed read loop —
  there is no separate client/server class on the hot path.
- **Transport- and codec-neutral.** The same contract runs over named-pipe, TCP, WebSocket, or an
  in-process channel with a swappable codec; the generated proxy, dispatcher, and `Provide`/`Get`
  extensions are identical either way.
- **A trusted channel, not a sandbox.** A provided service is callable by *any* peer on the channel, so
  enforce access control at the transport or application layer. The real trust boundary for untrusted
  author logic is Kernels / [Pushdown](/concepts/pushdown/), not Services.

**When to use Services:** a discrete, typed request→response you can `await`; a bounded number of calls
(one method = one round-trip); host↔plugin callbacks on one connection; or when you need Unity/IL2CPP
reach (Services is the most-mature netstandard2.1 surface).

**When to prefer another mode:** to react to a high-frequency server event but only need a filtered
subset, prefer the [event pipeline (RunLocal)](/tutorials/event-pipeline-runlocal/) — `Where` /
`Select` lower to server-side IR so only matching, projected values cross the pipe (one-way push, no
round-trips). To collapse a chatty N-call loop over the host's fine-grained bindings into one
server-side batch, prefer [Pushdown](/concepts/pushdown/) — the batch runs as verified, capability-gated,
fuel-metered IR.

Diagnostics from the generator use the `DBXS###` prefix — see
[reference/diagnostics.md](/reference/diagnostics/).

**See also:** the annotated [GameServer walkthrough](/examples/gameserver-walkthrough/) for a
guided tour, or the raw GameServer sample under
[`samples/GameServer`](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer), the
[Channels (RPC) guide](/channels/quick-start/) (quick-start, API reference, Unity integration, transports,
performance), [pushdown](/concepts/pushdown/) for composing services server-side, and
[`docs/examples/coverage-gaps.md`](/examples/coverage-gaps/) for service scenarios no longer shown
by maintained samples.
