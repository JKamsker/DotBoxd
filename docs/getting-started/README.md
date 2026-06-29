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

Or reference individual packages — see the table in the root [README](../../README.md).

## First Service (RPC)

1. Define a contract and annotate it with `[DotBoxDService]`.
2. Implement it on the host and `Provide…` it on each accepted peer.
3. Connect from the client and call the generated typed proxy.

The maintained runnable sample uses the same generated service pattern for its plugin control plane:
[`samples/GameServer/Examples.GameServer.Server`](../../samples/GameServer/Examples.GameServer.Server).
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
