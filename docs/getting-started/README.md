# Getting started

## Prerequisites

- .NET SDK **10.0.2xx** (pinned in `global.json`). The test suite also exercises the **.NET 8** and
  **.NET 9** runtimes, so install those runtimes if you intend to run all tests.
- Any OS: Windows, Linux, macOS.

## Install

```bash
# Full net10.0 stack (Services + Kernels + Pushdown):
dotnet add package DotBoxD

# Service / Unity (netstandard2.1) bundle only:
dotnet add package DotBoxD.Services.All
```

Or reference individual packages — see the table in the root [README](https://github.com/JKamsker/DotBoxD/blob/main/README.md).

## Why DotBoxD? (and which mode to use when)

**The problem it solves.** Host↔plugin communication usually forces a choice between two bad options:
hand-written marshaling (request envelopes, arg serialization, matching each response back to its call —
repetitive and easy to get subtly wrong), or a chatty stream where the host ships every event / exposes
only fine-grained calls and the client loops and filters after the fact. DotBoxD starts from **one C#
contract** and lowers it at compile time — no runtime reflection on the hot path — into whichever of
**three delivery strategies** fits the call shape. The interface stays the single source of truth: a
contract-shape mismatch is a compile error, not a wire fault.

**The payoff: pick the strategy by call shape, keep one authoring model.**

- **Services (RPC) = easy interop.** One `[DotBoxDService]` interface compiles to a typed proxy plus a
  dispatcher, so there is no hand-written marshaling and no runtime reflection on the hot path — the
  netstandard2.1 stack AOTs and runs on Unity / IL2CPP. Reach for it for a discrete, typed
  request→response you can `await`, a bounded number of calls (one method = one round-trip), or
  host↔plugin callbacks over one connection. See [concepts/services.md](../concepts/services.md).
- **Query / event pipeline (RunLocal) = efficient server-side filtering + projection.** `Where` /
  `Select` lower to verified IR that runs next to the data, so only matching, projected values cross the
  pipe — fewer bytes, fewer wake-ups, one-way push, no round-trips. Because that filter logic runs as
  validated, fuel-metered IR, it is safe to accept from untrusted plugins. Reach for it to react to a
  high-frequency server event but consume only a filtered/shaped subset. See
  [tutorials/event-pipeline-runlocal.md](../tutorials/event-pipeline-runlocal.md).
- **Pushdown = reduce round-trips.** Move the loop/aggregation next to the data so N per-entity calls
  collapse into one server-side batch. The host stays frozen and minimal while plugins add batch ops
  without recompiling it, and the batch runs as verified, capability-gated, fuel-metered IR. Reach for
  it when a client would otherwise loop with one round-trip per entity against a frozen host. See
  [concepts/pushdown.md](../concepts/pushdown.md).

RunLocal and Pushdown share the same sandbox foundation ([concepts/kernels.md](../concepts/kernels.md)):
untrusted author C# is lowered to validated, fuel-metered **kernel IR**, so plugin logic never runs as
trusted CLR code. Services, by contrast, is a **trusted channel** — a provided service is callable by any
peer, so enforce access control at the transport or application layer. The three quickstarts below map
one-to-one onto these strategies.

## First Service (RPC)

1. Define a contract and annotate it with `[DotBoxDService]`.
2. Implement it on the host and `Provide…` it on each accepted peer.
3. Connect from the client and call the generated typed proxy.

The maintained runnable sample uses the same generated service pattern for its plugin control plane:
[`samples/GameServer/Examples.GameServer.Server`](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer/Examples.GameServer.Server).
See [concepts/services.md](../concepts/services.md).

## First Kernel (sandbox)

1. Create a `SandboxHost` with the bindings you want to expose.
2. Build a `SandboxPolicy` (fuel, loop, list, capability budgets).
3. Import the kernel JSON IR, `PrepareAsync`, then `ExecuteAsync`.

See the GameServer sample and [concepts/kernels.md](../concepts/kernels.md).

## Pushdown quickstart

Expose a contract method that composes host data and runs a validated kernel server-side, so the client
submits work in one round-trip instead of N. The GameServer sample demonstrates this with the
`MonsterKillerKernel` server extension. See [concepts/pushdown.md](../concepts/pushdown.md).

## Run the maintained example

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

It demonstrates service IPC, event kernels, live settings, host bindings, policy-gated execution,
server extensions, and unload-on-disconnect. Features no longer covered by maintained samples are listed in
[`docs/examples/coverage-gaps.md`](../examples/coverage-gaps.md).
