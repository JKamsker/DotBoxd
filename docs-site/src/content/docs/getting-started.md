---
title: 'Getting started'
---
## Prerequisites

- .NET SDK **10.0.2xx** (pinned in `global.json`). The test suite also exercises the **.NET 8** and
  **.NET 9** runtimes, so install those runtimes if you intend to run all tests.
- Any OS: Windows, Linux, macOS.

## Install

```bash
# Full net10.0 stack (Services + Kernels + Pushdown):
dotnet add package DotBoxD --prerelease

# Service / Unity (netstandard2.1) bundle only:
dotnet add package DotBoxD.Services.All --prerelease
```

> `--prerelease` is required while the net10.0 stack is in preview; drop it once a stable tag ships.

Or reference individual packages — see the table in the root [README](https://github.com/JKamsker/DotBoxD/blob/main/README.md).

## Why DotBoxD? (and which mode to use when)

Host↔plugin communication usually forces a choice between hand-written marshaling (request envelopes and
matching each response back to its call — repetitive, easy to get subtly wrong) and a chatty stream the
client has to filter after the fact. DotBoxD starts from **one C# contract** and lowers it (compiles it
down to a restricted lower-level form) at compile time — no runtime reflection on the hot path — into
whichever of **three delivery strategies** fits the call shape:

- **Services (RPC):** typed request→response.
- **Query (RunLocal):** server-side filter + projection, one-way push to the plugin.
- **Pushdown:** collapse N per-entity round-trips into one server-side batch.

RunLocal and Pushdown run author logic as validated, fuel-metered [kernel IR](/concepts/kernels/) (never trusted CLR code);
Services is a trusted channel you guard at the transport or application layer. See
[Choosing a mode](/overview/#choosing-a-mode) for the full comparison and the
[glossary](/reference/glossary/) for unfamiliar terms — the three quickstarts below map one-to-one
onto these strategies.

## First Service (RPC)

1. Define a contract and annotate it with `[DotBoxDService]`.
2. Implement it on the host and `Provide…` it on each accepted peer.
3. Connect from the client and call the generated typed proxy.

The maintained runnable sample uses the same generated service pattern for its plugin control plane:
[`samples/GameServer/Examples.GameServer.Server`](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer/Examples.GameServer.Server).
See [concepts/services.md](/concepts/services/), or [build it step by step](/tutorials/first-service/).

## First Kernel (sandbox)

1. Create a `SandboxHost` with the bindings you want to expose.
2. Build a `SandboxPolicy` (fuel, loop, list, capability budgets).
3. Import the kernel JSON IR, `PrepareAsync`, then `ExecuteAsync`.

See the GameServer sample or [concepts/kernels.md](/concepts/kernels/). For a concrete runnable version
of this raw flow — `SandboxHost.Create` → `ImportJsonAsync` → `PrepareAsync` → `ExecuteAsync` under a policy —
see section 2 (Kernels) of the root [README](https://github.com/JKamsker/DotBoxD/blob/main/README.md), or a
real [JSON-IR example](https://github.com/JKamsker/DotBoxD/blob/main/docs/Specs/Initial/dotboxd-sandbox-spec/examples/example-ir.md).
For the fluent event-pipeline equivalent — `server.Hooks` over a `GamePluginServerBuilder`, not the raw
`SandboxHost` steps above — [build it step by step](/tutorials/event-pipeline-runlocal/).

## Pushdown quickstart

Expose a contract method that composes host data and runs a validated kernel server-side, so the client
submits work in one round-trip instead of N. The GameServer sample demonstrates this with the
`MonsterKillerKernel` server extension. See [concepts/pushdown.md](/concepts/pushdown/), or
[build it step by step](/tutorials/pushdown-server-extension/).

## Run the maintained example

The sample lives in the repo, not the NuGet package — `git clone https://github.com/JKamsker/DotBoxD` and
run from the repo root:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

You should see three phases print — a baseline run, a with-plugins run, and a summary confirming the
plugin's kernels unloaded on disconnect. For the annotated output, see
[What the run prints](/examples/gameserver-walkthrough/#what-the-run-prints).

It demonstrates service IPC, event kernels, live settings, host bindings, policy-gated execution,
server extensions, and unload-on-disconnect. Features no longer covered by maintained samples are listed in
[`docs/examples/coverage-gaps.md`](/examples/coverage-gaps/).

## Next steps

Ready for an end-to-end walkthrough? Work through the [tutorials](/tutorials/).
