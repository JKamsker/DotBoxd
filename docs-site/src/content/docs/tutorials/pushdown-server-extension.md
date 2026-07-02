---
title: 'Tutorial: Pushdown — ship a server-side batch operation'
description: 'This tutorial walks through Pushdown, the third way DotBoxD lets a host and its clients share one C# contract. You will take a host that exposes only a…'
---
This tutorial walks through **Pushdown**, the third way DotBoxD lets a host and its clients share one C# contract. You will take a host that exposes only a *fine-grained* binding ("kill one monster") and, **without recompiling the host**, ship a plugin that adds a *batch* aggregate ("kill these N monsters") which runs server-side and collapses N remote round-trips into **one**.

The payoff: the batch method is plain C#, but the analyzer lowers it to the same verified, capability-gated, fuel-metered IR that event kernels run under. It is untrusted-author code running under a real sandbox — not a trusted plugin with full CLR access.

Everything below uses the real, compiling API. The canonical snippet lives in [`README.md`](https://github.com/JKamsker/DotBoxD/blob/main/README.md) (section "3. Pushdown"); the runnable example is the GameServer sample under [`samples/GameServer`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer).

## Why Pushdown? (and when to use it)

**The problem it beats.** A client acting on many entities against a fine-grained host is forced into a client-side loop where *every iteration is a network hop* — for N monsters that is N remote calls, N serialization passes, and N chances for latency to dominate. The only other "fix" would be to bloat the host with every conceivable batch method, but the host is frozen at release and ships no batch operations, and a bespoke trusted per-plugin endpoint is impossible because the server cannot be recompiled ([`docs/concepts/pushdown.md`](/concepts/pushdown/)). Pushdown replaces the chatty loop instead: the plugin ships a `[ServerExtension]` batch that loops the host's existing bindings server-side, so **N round-trips collapse into one**, and only one compact result crosses back.

**The payoff, grounded:**

- **Fewer round-trips, less serialization.** Each avoided hop removes one network RTT, one request+response serialization pair, and one opportunity for tail latency — the win grows the higher the link latency. The sample's `MonsterKillerKernel` does five reads/writes per entity inside one server-side loop; client-side that is ~5N round-trips, server-side it is one, because "the awaited calls are local (no real IPC hop)" ([`MonsterKillerKernel.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Kernels/MonsterKillerKernel.cs)).
- **The host stays frozen, minimal, and trusted.** Fine-grained single-entity bindings are the safe primitives it exposes; the coarse batch lives in untrusted, sandboxed plugin code instead of enlarging the trusted host. New batch operations are added *after* deployment by installing a plugin — the server is never recompiled.
- **Composability over existing primitives.** The batch composes bindings the host already exposes into an operation the host author never anticipated (`RangeMonsterKillerKernel` reuses the same reads/writes plus a spatial predicate and a result cap), using only the lowering surface: `foreach`, `if`/`else`, locals, host-binding calls, DTO construction, and `List<T>` accumulation.
- **Untrusted-author code, real sandbox.** Because the batch lowers to verified, capability-gated, fuel-metered IR — reaching only registered `[HostBinding]`s, not everything reachable via normal RPC — you can accept batch logic from untrusted plugin authors safely; the boundary is the kernel, not a trusted assembly load ([`docs/security/sandbox-caveats.md`](/security/sandbox-caveats/)).

**When to use it.** The host is frozen and fine-grained, but a client needs a coarse operation over many entities and the workload is latency-bound; the batch fits the lowering surface and returns a single compact result.

**When to prefer another mode:**

- Need a one-way, no-return *push* of a filtered/shaped event stream to your plugin? Use the [event pipeline (RunLocal)](/tutorials/event-pipeline-runlocal/) — same "run author logic server-side" idea, but push-to-plugin instead of aggregate-and-return.
- Just calling one already-coarse host capability request/response? Use a plain [Service (RPC)](/concepts/services/) — there are no N round-trips to collapse.
- You control the host and it isn't frozen? Add the batch method to the host directly — Pushdown's premise is the *inability* to recompile it.
- Need streaming/incremental results, arbitrary CLR calls, or hard multi-tenant isolation against fully arbitrary .NET code? Those are outside the model — Pushdown returns a single value, only lowers the supported shapes, and defends the in-process boundary, not an OS one ([`docs/security/sandbox-caveats.md`](/security/sandbox-caveats/)).

## What you'll build

- A **host** interface `IGameWorld` that exposes exactly one fine-grained `[HostBinding]`: `Kill(int id)`.
- A **plugin** that declares a batch contract `IMonsterKillerService` and a `[ServerExtension(...)]` partial class `MonsterKillerKernel` whose method loops over the host binding.
- A **caller** that installs the extension once and invokes the whole batch in a single round-trip.

## Prerequisites

> This tutorial teaches the **plugin-side authoring shapes**; the host `PluginServer` (the `server` used in Step 5) is not built here — run the pattern via the maintained GameServer sample (Step 7 below), or scaffold your own host project as in [first-service](/tutorials/first-service/) Step 1.

Pushdown lives on the net10.0 Kernels/Plugins stack. Add the authoring contracts, the host runtime, and the generator/analyzer (and the IPC addon if the plugin and host are in separate processes):

```bash
# Plugin-to-host authoring contracts: [HostBinding], [ServerExtension], HookContext
dotnet add package DotBoxD.Abstractions --prerelease

# Host runtime that loads, validates, and dispatches plugins (PluginServer)
dotnet add package DotBoxD.Plugins --prerelease

# Source generator + analyzer that lowers [ServerExtension] kernels to verified IR
dotnet add package DotBoxD.Plugins.Analyzer --prerelease

# (Cross-process only) MessagePack IPC addon that runs kernels next to host services
dotnet add package DotBoxD.Pushdown.Services --prerelease
```

Package names and purposes are from the README "Installing from NuGet" table ([`README.md`](https://github.com/JKamsker/DotBoxD/blob/main/README.md)).

## Step 1 — The problem: a frozen host with only fine-grained bindings

A shipped host is usually **frozen at release**. It exposes small, single-entity operations because those are the safe primitives it wants to expose to the sandbox — it deliberately ships *no* batch operations.

A client that needs to act on many entities is then forced into a loop, and every iteration is a network hop:

```text
client -> Kill(1)
client -> Kill(2)
...           (N round-trips)
```

For N monsters that is N remote calls, N serialization passes, and N chances for latency to dominate. You cannot add `KillMonsters(...)` to the host — it is already released. This is exactly the situation Pushdown is designed for (see [`docs/concepts/pushdown.md`](/concepts/pushdown/)).

## Step 2 — Host declares a fine-grained `[HostBinding]` with capability + effect

The host exposes its single-entity primitive as a method marked with `[HostBinding]`. This attribute is the whole contract the sandbox sees: a binding id the call lowers to, the **capability** it requires, and the **`SandboxEffect`** it declares.

```csharp
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;

// The host (frozen at release) exposes only a fine-grained binding — there is NO batch method here.
public interface IGameWorld
{
    [HostBinding("host.world.kill", "game.world.monster.write.kill",
                 SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
    bool Kill(int id);
}
```

What each argument means (from the `HostBindingAttribute` docs in [`src/Hosting/DotBoxD.Abstractions/Contracts.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Hosting/DotBoxD.Abstractions/Contracts.cs)):

- **`"host.world.kill"`** — the sandbox binding id. The generator lowers any `ctx.Host<IGameWorld>().Kill(id)` call to a `CallExpression("host.world.kill", …)`.
- **`"game.world.monster.write.kill"`** — the capability recorded in the plugin manifest's required capabilities. A kernel that touches this binding only installs under a policy that grants that capability.
- **`SandboxEffect.Cpu | SandboxEffect.HostStateWrite`** — the effect set added to the manifest. `SandboxEffect` is a `[Flags]` enum in [`src/Kernels/DotBoxD.Kernels/Sandbox/SandboxEffect.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Kernels/DotBoxD.Kernels/Sandbox/SandboxEffect.cs); a write binding declares exactly one of `HostStateRead`/`HostStateWrite`.

The host registers a matching binding at startup (same id, capability, and effects) so **install-time policy and effect validation gate the call**. If the plugin's manifest asks for a capability the host policy did not grant, install fails — the code never runs.

## Step 3 — Plugin defines a batch contract + a `[ServerExtension]` partial class

Now the plugin adds what the host never shipped. First the batch **contract** — an ordinary interface plus the DTO it returns:

```csharp
using System.Collections.Generic;

// A PLUGIN adds its own batch aggregate. KillMonsters does not exist on the host — the plugin ships it.
public interface IMonsterKillerService
{
    List<KillResult> KillMonsters(List<int> monsterIds);
}

public readonly record struct KillResult(int MonsterId, bool Success);
```

Then the **kernel**: a `partial class` marked `[ServerExtension("id", typeof(TContract))]` with one public batch method. Its body loops over the list parameter and calls the host's existing binding through `ctx.Host<IGameWorld>()`:

```csharp
using System.Collections.Generic;
using DotBoxD.Abstractions;

[ServerExtension("monster-killer", typeof(IMonsterKillerService))]
public sealed partial class MonsterKillerKernel
{
    public List<KillResult> KillMonsters(List<int> monsterIds, HookContext ctx)
    {
        var results = new List<KillResult>();
        foreach (var id in monsterIds)
            results.Add(new KillResult(id, ctx.Host<IGameWorld>().Kill(id))); // calls the host's existing binding
        return results;
    }
}
```

Notes that make this compile and lower correctly (all from the `ServerExtensionAttribute` and `HookContext` docs in [`src/Hosting/DotBoxD.Abstractions/Contracts.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Hosting/DotBoxD.Abstractions/Contracts.cs)):

- The trailing **`HookContext ctx`** parameter is the *lowering marker* for host bindings (exactly as in a kernel's `Handle`). It is **not** part of the wire signature — the contract method is just `KillMonsters(List<int>)`.
- **`ctx.Host<IGameWorld>()`** is never actually invoked at runtime; calling it directly throws `NotSupportedException`. The generator replaces the call with the `host.world.kill` binding.
- The body may use locals, a `foreach` over a list parameter, host bindings, and may build and return complex objects (records/DTOs) and lists of them — complex values ride the IR `Record` type.
- Passing the optional **service type** (`typeof(IMonsterKillerService)`) lets the analyzer emit a source-generated plugin-side client that marshals directly to compact server-extension value bytes instead of using a reflection proxy.

This exact shape is exercised as a compiling test fixture in [`tests/DotBoxD.Kernels.Tests/Plugins/Rpc/ServerExtension/ServerExtensionProxyTests.cs`](https://github.com/JKamsker/DotBoxD/blob/main/tests/DotBoxD.Kernels.Tests/Plugins/Rpc/ServerExtension/ServerExtensionProxyTests.cs).

> The GameServer sample takes this one step further: its [`MonsterKillerKernel`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Kernels/MonsterKillerKernel.cs) grafts onto a domain control with `[ServerExtension(typeof(IMonsterControl))]` and injects the world as a constructor field (`_world.Monsters.Get(id)`) instead of `ctx.Host<T>()`. Constructor-injected host services and `ctx.Host<T>()` are two spellings of the same lowering marker — pick whichever reads best.

## Step 4 — The analyzer lowers your C# to verified, capability-gated, fuel-metered IR

This is the part that makes Pushdown safe. Your `KillMonsters` method looks like ordinary C#, but it is **not** compiled to IL and run as trusted code. The `DotBoxD.Plugins.Analyzer` source generator lowers the method body into restricted **kernel IR** — the same JSON IR shape the Kernels stack validates and meters — and bakes it into the plugin package.

At install and run time that IR goes through the identical pipeline as an event kernel:

1. **Structural + type validation** — only supported statements/expressions (`foreach`, `if`/`else`, locals, host-binding calls, DTO construction, `List<T>` accumulation) survive.
2. **Capability gating** — every `[HostBinding]` call and `[Capability]`-gated read the body touches contributes its capability to the manifest; install fails unless the host policy grants them.
3. **Effect validation** — the manifest's declared effects must match the verified entrypoint's effects (a `HostStateWrite` binding cannot masquerade as pure).
4. **Fuel/quota metering** — the loop runs under the host's fuel budget, max loop iterations, and max list length, so a hostile or buggy batch cannot run away with host resources.

The trust model is spelled out in the README: the batch logic "runs as a validated sandboxed kernel under the same trust model as event kernels: it can reach only the host bindings the server already exposes, gated by capabilities and fuel/quota limits" ([`README.md`](https://github.com/JKamsker/DotBoxD/blob/main/README.md), section 3). Critically, **a method reachable via normal RPC is not automatically reachable from a kernel** ([`docs/concepts/pushdown.md`](/concepts/pushdown/)) — the only surface the kernel can touch is the set of registered `[HostBinding]`s.

## Step 5 — Install once, invoke once (N round-trips → 1)

On the host side, `PluginServer` (from `DotBoxD.Plugins`) resolves the kernel's generated verified-IR package, installs it as a server extension, and binds it to the contract. Then any caller gets a typed proxy and calls the batch in **one** round-trip. Here `server` is that `PluginServer` instance — the one built in the [GameServer walkthrough](/examples/gameserver-walkthrough/):

```csharp
using DotBoxD.Plugins;

// The host's PluginServer already has the IGameWorld binding registered (same id/capability/effects).
// Install the plugin's kernel under the IMonsterKillerService contract:
await server.RegisterServerExtensionAsync<IMonsterKillerService, MonsterKillerKernel>();

// The caller invokes the whole batch in ONE round-trip, not N:
var ids = new List<int> { 1, 2, 3 };
List<KillResult> killed = server.ServerExtension<IMonsterKillerService>().KillMonsters(ids);
```

The signatures are exactly as declared in [`src/Hosting/DotBoxD.Plugins/Runtime/Rpc/PluginServer.Rpc.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Hosting/DotBoxD.Plugins/Runtime/Rpc/PluginServer.Rpc.cs):

- `ValueTask<string> RegisterServerExtensionAsync<TService, TKernel>(SandboxPolicy? policy = null, CancellationToken cancellationToken = default)` — resolves `TKernel`'s generated package, installs it, binds `TService` to it, and returns the plugin id. Pass an optional `SandboxPolicy` to tighten fuel/capability limits beyond the host default.
- `TService ServerExtension<TService>()` — returns the typed proxy; it throws `InvalidOperationException` if you call it before registering.

The two lines above are asserted verbatim against the README by a regression test ([`tests/DotBoxD.Kernels.Tests/Hosting/Regression/Fix_API_0027_Tests.cs`](https://github.com/JKamsker/DotBoxD/blob/main/tests/DotBoxD.Kernels.Tests/Hosting/Regression/Fix_API_0027_Tests.cs)), and the register-then-invoke round-trip is covered end to end in `ServerExtensionProxyTests.RegisterServerExtension_then_ServerExtension_invokes_the_batch_kernel_by_contract`.

Across a process boundary, the same install + a compact binary IR invoke payload are forwarded by the `DotBoxD.Pushdown.Services` MessagePack IPC addon — the plugin authoring code is identical.

## Step 6 — Diagnostics and the no-lock-in principle

**Diagnostics.** If your batch method uses a shape the lowering surface cannot represent, the analyzer fails the generation *safely* (it does not miscompile) and reports a diagnostic in the kernels/plugins namespace **`DBXK###`** (services diagnostics use `DBXS###`). For example, `DBXK115` rejects duplicate generated server-extension graft signatures, and `DBXK116` rejects `[Local]` context helpers reaching lowered server-side IR (see [`src/CodeGeneration/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Unshipped.md`](https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Unshipped.md)). Treat a `DBXK###` error as "the sandbox does not support this construct," not as a bug to work around.

**No lock-in.** The `[ServerExtension]` attribute and the generator are **opt-in sugar over public primitives, never lock-in** ([`CLAUDE.md`](https://github.com/JKamsker/DotBoxD/blob/main/CLAUDE.md), [`rules/design-guidelines.md`](https://github.com/JKamsker/DotBoxD/blob/main/rules/design-guidelines.md)). Everything the generator produces is verified JSON IR plus a manifest — both first-class, public artifacts. You could delete the attribute and hand-author the same IR, then import and run it through the same `SandboxHost` pipeline the Kernels quick-start uses (`ImportJsonAsync` → `PrepareAsync` under a `SandboxPolicy` → `ExecuteAsync`, as shown in [`README.md`](https://github.com/JKamsker/DotBoxD/blob/main/README.md) section 2). The generator saves you from writing IR by hand; it does not grant your code any capability a hand-written kernel could not also request — and it never bypasses validation, capability gating, or metering. If you can't hand-write it against the public API, the generator won't emit it either.

## Step 7 — Run the maintained example

The `server` from Step 5 lives in the maintained GameServer sample, which wires the `PluginServer`, registers the `IGameWorld` binding, and installs the `MonsterKillerKernel` server extension. Run it from the repo root:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

What you should see: the sample host boots, loads the plugin, and exercises the batch server extension end to end — the same install-once/invoke-once path from Step 5 — alongside the other modes (service IPC, event kernels, host bindings, policy-gated execution).

## Next steps

- [Event pipelines (RunLocal)](/tutorials/event-pipeline-runlocal/) — the sibling tutorial: the *other* way author logic runs server-side — a `Where`/`Select` filter lowered to the same verified IR, reacting locally instead of aggregating.
- [Pushdown, in depth](/concepts/pushdown/) — the concept, the round-trip diagrams, and the roadmap items (`DotBoxD.Pushdown.Linq`, fluent client API).
- [Sandbox caveats](/security/sandbox-caveats/) — what is and isn't a trust boundary before you deploy a server extension that runs author-supplied logic.
- [GameServer walkthrough](/examples/gameserver-walkthrough/) — all three modes (Services, RunLocal, Pushdown) working together in one runnable program.
