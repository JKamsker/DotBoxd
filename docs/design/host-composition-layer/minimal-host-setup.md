# Host Composition Layer — minimal server setup with good defaults

> Status: **implemented** on branch `feat/host-composition-layer`. Follow-up to #87/#88, **separate** from those.
> Goal: make the *server* author write mostly domain + ordinary C#, with DotBoxD setup that is
> minimal-by-default, deeply extensible, and that preserves the trust boundary.

## As-built (deltas from the design below)

All four pieces shipped as **library** code. Notable deltas:

- **Piece 1 router** — exposed as **two methods** `PluginServer.WireHook` / `PluginServer.WireSubscription`, not a single `Wire`: the host's IPC contract already distinguishes hook vs subscription installs and no manifest field can infer it. `KernelWireTerminal.Classify` and `ErasedPluginEventAdapter<TEvent>` are **internal**; `IErasedPluginEventAdapter`, `KernelWireKind`, `KernelWireTerminal`, `WireOptions`, `WireCallbacks` are public.
- **By-name resolution requires pre-registered adapters.** The router resolves the subscribed event by name and does **not** auto-register (it has no `Type`), so the host declares its supported events once — the sample's wiring policy calls `server.Events.Resolve<T>()` per supported event, which doubles as the supported-event allowlist. `WireOptions.IndexRegistry` is the seam for the **world-owned** `EventIndexRegistry` (the index is not server-owned).
- **Piece 4** — `PluginSession.InstallAndWireAsync(PluginPackage, Action<InstalledKernel> wire, policy?, validate?, ct)`; the `wire` delegate is the host's `WireHook`/`WireSubscription` choice; rollback uninstalls by exact `InstallId` (a same-id incumbent is never disturbed); `validate` runs before install.
- **Piece 2 — runtime helper, NOT a source generator.** The generated host was assessed feasible but highest-risk / smallest-win (it would attach a generator to the Server project, which has none, and couple the generated surface to `Program.cs`). Instead `PluginConnectionHost<TConnection>` (a runtime helper in `DotBoxD.Pushdown.Services` — the existing "kernels-meet-RPC" integration layer, which gains a `DotBoxD.Plugins` reference) owns the per-connection lifecycle: listen, mint a session per peer, **dispose-on-disconnect**, and surface `Connected`/`Disconnected`/`PipeName`. The host keeps only the connection-specific `(peer, session) => provide services` callback. `DotBoxD.Plugins` stays transport-agnostic (the helper lives in the IPC layer, not Plugins).

Result: `GamePluginKernelWiring` 256 → ~130 (host policy), `GamePluginServerExtensionInvoker` (68) deleted, the install/rollback helper (35) deleted, `GamePluginHost` 73 → ~36 (thin factory). New public surface in `docs/api-baselines/DotBoxD.Plugins.txt` + `DotBoxD.Pushdown.Services.txt`. All trust-boundary tests + the GameServer docs-smoke e2e are green.

## Composition & escape hatches

Per [`rules/design-guidelines.md`](../../../rules/design-guidelines.md), every helper here is **opt-in sugar over public primitives** — you can always drop a helper and hand-write the same thing with public API (the code this PR deleted is the proof; it used only public API). Each helper sits in a layered, independently usable surface:

| Helper | Customize it | Hand-write the equivalent (all public) |
|---|---|---|
| `PluginServer.WireHook` / `WireSubscription` | `WireOptions.ClassifyOverride`; resolve via `PluginEventAdapterRegistry.TryResolveErased` and wire your own way | `server.Hooks.On<TEvent>().Use/UseProjecting/…(kernel)` + your own event-name → type dispatch |
| `PluginSession.InstallAndWireAsync` | pass any `wire` action / `policy` / `validate` | `session.InstallAsync` + wire + `session.Uninstall` on failure |
| `InstalledKernel.InvokeServerExtensionRpcAsync` | — | `kernel.InvokeServerExtensionAsync` + `KernelRpcBinaryCodec` / `KernelRpcValueConverter` |
| `PluginConnectionHost<T>` | `StartAsync(server, IServerTransport, …)` for any transport; `RpcPeerOptions` | `RpcMessagePackIpc.Listen` + `server.CreateSession` + `peer.Provide*` + `peer.Disconnected` |

No helper is all-or-nothing, none gates access to a lower layer, and a future `[GeneratePluginServerHost]` generator must follow the same rule (granular per-facet opt-out; a generate-vs-hand-write parity test).

## 1. Problem

The **plugin** author already hits the target shape:

- `GamePluginServer.cs` — one line: `[GeneratePluginServer(Context = typeof(GamePluginContext))] public partial class GamePluginServer : IGameWorldAccess;`
- `GamePluginContext.cs` — pure domain.
- `Program.cs` — `Setup(s => s.Hooks.On<MonsterAggroEvent>().Use<GuardianKernel>())`, fluent `.Where().Select().Run()`.

The **server** author does not. ~400 lines of DotBoxD plumbing are hand-written across four files:

| Server file | Lines | What it is | Verdict |
|---|---|---|---|
| `Ipc/GamePluginKernelWiring.cs` | 256 | manifest introspection + hand-written `MatchesEvent<MonsterAggroEvent>` else `<AttackEvent>` switch + terminal-kind routing (`Use`/`UseProjecting`/`UseResult`/`UseProjectingResult`) | pure ceremony, worst offender |
| `Ipc/GamePluginControlService.cs` | 204 | three near-identical install→validate→policy→install→wire→rollback methods + reflection-friendly ctor overloads | ~70% ceremony |
| `Ipc/GamePluginHost.cs` | 73 | per-connection: mint session, get reverse callback, `Provide*` two services, dispose on disconnect | pure ceremony |
| `Ipc/GamePluginServerExtensionInvoker.cs` | 68 | decode RPC args → count vs live-settings → convert each `SandboxValue` → invoke → encode | pure marshalling |
| `Ipc/GameWorldAccess.cs` | 126 | `KillAsync() => world.KillMonster(Id)` + `[HostCapability]` | **domain — the good model** |

### Root cause

The framework ships **primitives** (`session.InstallAsync`, `server.Hooks.On<T>().Use(kernel)`,
`RpcMessagePackIpc.ListenNamedPipe`, generated `Provide*`, `server.GetRequiredCapabilities`) but **not the
host-side composition** that strings them into the one default flow every host needs: *accept a connection →
install a package → route its kernel to the right typed pipeline by terminal kind*.

The sharpest symptom: `server.Events.Resolve<TEvent>()` is **generic-by-type**, so the host has no way to go
from a manifest event-name string to a typed `On<TEvent>()` call except a hand-written switch — and every new
event type adds another branch in two methods. `PluginEventAdapterRegistry.TryResolveShape(string)` already
proves by-name lookup is feasible, but it is `internal` and returns a descriptive shape, not a wire-capable
adapter.

### This is host-owned on purpose — which argues *for* the fix

The host recomputing terminal kind / effects / index coverage from verified IR is the **trust boundary**
working as designed (the manifest is never trusted). The fix is **not** to let the plugin drive wiring. It is
to make that recompute **one framework-provided, audited helper** every host calls, instead of copy-pasted
(and potentially mis-implemented) into each host. That is strictly better for security *and* collapses the
ceremony. The two goals do not conflict.

## 2. Goals / non-goals

**Goals**
- Server code reads as domain (`GameWorldAccess`) + minimal setup.
- Defaults that "just work"; every mechanical step has a good default.
- Deeply extensible: the host can override routing, policy, and service construction at clean seams.
- Preserve every trust-boundary invariant; provide a single audited home for the recompute.

**Non-goals**
- Changing the plugin authoring surface (already minimal).
- Moving any trust decision onto the manifest.
- Removing the host's ability to override routing/policy (defaults, not lock-in).

## 3. Design — four pieces

### Piece 1 — Kernel router: `server.Wire(kernel)` (deletes `GamePluginKernelWiring`, ~256 lines)

> **Shipped as `PluginServer.WireHook` / `PluginServer.WireSubscription`** (two methods, not a single `Wire`) —
> see the [As-built](#as-built-deltas-from-the-design-below) note. The sketch below writes `Wire`/`server.Wire`;
> read it as those two methods.

Two framework additions plus one composition method.

**(a) Type-erased, wire-capable adapter resolution.** When `RegisterEventAdapter<TEvent>` runs, `TEvent` is
statically known — so capture the typed wire closures *then* and store them in the registered record. The
router resolves by the kernel's verified event name and gets back something it can wire **without reflection**:

```csharp
// new public surface on the registry
public bool TryResolveErased(string eventName, out IErasedPluginEventAdapter adapter);

public interface IErasedPluginEventAdapter
{
    Type EventType { get; }
    string EventName { get; }
    // captured at Register<TEvent> time; closes over the static TEvent
    void WireHook(HookRegistry hooks, InstalledKernel kernel, KernelWireTerminal terminal, WireCallbacks callbacks);
    void WireSubscription(SubscriptionRegistry subs, InstalledKernel kernel, KernelWireTerminal terminal, WireCallbacks callbacks);
}
```

**(b) One trusted terminal classification** computed once from install-owned + verified metadata, replacing
the sample's `IsLocalTerminal` / `IsResultHook` / `IsResultLocalTerminal` / `Priority` / `HookResultTypeFor`:

```csharp
public enum KernelWireKind { Plain, Projecting, Result, ProjectingResult }
public readonly record struct KernelWireTerminal(KernelWireKind Kind, string? CallbackSubscriptionId, Type? ResultType, int Priority);
```

It reads `kernel.CallbackSubscriptionId` (install-owned, from `Package`) and the verified subscription flags —
the data the sample already reads, but in the framework, audited once.

**(c) The composition method:**

```csharp
public void Wire(InstalledKernel kernel, WireOptions? options = null);
```

resolves the adapter by the kernel's verified event, classifies the terminal, and routes to
`Hooks`/`Subscriptions` with the right `Use*`. Default-on index routing folds in `TryRouteThroughIndex`.

**Host's only real seam** — the genuinely host-specific bits stay explicit and nothing else does:

```csharp
public sealed record WireOptions(
    RemoteLocalPush? LocalPush = null,                 // remote RunLocal callback
    RemoteLocalResultRequest? LocalResult = null,      // remote RegisterLocal callback
    bool UseIndex = true,                              // default-on prefilter
    Func<KernelWireTerminal, KernelWireTerminal>? ClassifyOverride = null);
```

**Trust:** classification reads verified/install-owned data; the verified `ShouldHandle` still runs after any
index survivor (index is prefilter only). One audited implementation replaces N hand-written copies.

### Piece 2 — Generated connection host (deletes `GamePluginHost`, ~73 lines)

> **Superseded — see the [As-built](#as-built-deltas-from-the-design-below) note.** This section is the
> *original* source-generator proposal. It shipped instead as a **runtime helper** (`PluginConnectionHost<T>`
> in `DotBoxD.Pushdown.Services`) — same per-connection lifecycle (session-per-peer, dispose-on-disconnect,
> `Connected`/`Disconnected`/`PipeName`), no generator, lower risk. The host keeps the `(peer, session) =>`
> provide-services callback. The generator design below is retained for the record / a future iteration.

Extend the `[GeneratePluginServer]` / `[DotBoxDService]` codegen to also emit a **host-side** accept helper
that mints a session per peer, resolves+provides every `[DotBoxDService]` impl from a host-supplied factory,
wires reverse callbacks, and disposes the session on disconnect:

```csharp
// generated
await using var host = GameWorldServerHost.Listen(
    pipeName, server,
    peer => new GameServices(world, sink, peer));   // the host's only seam: build the domain impls
```

Everything mechanical (pipe, session lifecycle, `Provide*`, reverse `Get*`, dispose-on-disconnect) is
generated. The factory is where the host injects domain state.

> Codegen risk: the Roslyn same-compilation blind spot + marker-attribute pattern resolved in #88 applies here.
> Spike this piece independently.

### Piece 3 — Generated server-extension invoker (deletes `GamePluginServerExtensionInvoker`, ~68 lines)

The decode→count→convert→invoke→encode dance is 100% mechanical from the manifest's `RpcEntrypoint` + the
live-settings count (already precomputed as `InstalledKernel._rpcCallerArgumentCount`). Promote it:

```csharp
public ValueTask<byte[]> InvokeServerExtensionRpcAsync(byte[] arguments, CancellationToken ct = default);
```

The codec lives in the framework. The **ownership check** (`kernel.OwnerId == session`) stays the host's —
it is an authz decision — but gets a one-line helper.

### Piece 4 — `session.InstallAndWireAsync(...)` (collapses the 3 install methods + rollback)

> **Shipped signature** (see the [As-built](#as-built-deltas-from-the-design-below) note): it takes a parsed
> `PluginPackage` and an explicit `wire` action (the host's `WireHook`/`WireSubscription` choice), not the
> string/`WireOptions` sketch below:
> ```csharp
> public ValueTask<InstalledKernel> InstallAndWireAsync(
>     PluginPackage package, Action<InstalledKernel> wire, Func<PluginPackage, SandboxPolicy>? policy = null,
>     Action<PluginPackage>? validate = null, CancellationToken cancellationToken = default);
> ```

One framework method: import (or accept a `PluginPackage`), compute least-privilege policy via
`GetRequiredCapabilities`, install through the session, `Wire`, and roll back (`Uninstall(installId)`) on any
failure:

```csharp
public ValueTask<InstalledKernel> InstallAndWireAsync(
    string packageJson, WireOptions? wire = null, Func<PluginPackage, SandboxPolicy>? policy = null,
    Action<PluginPackage>? validate = null, CancellationToken ct = default);
```

The three near-identical `Install*Async` methods in `GamePluginControlService` become one-liners; the control
service shrinks to its actual domain (`GetWorldAsync`, `DrainEffectsAsync`) + lifecycle. `validate` is the
host's route-validation seam (`ValidateRoute`); `policy` overrides the default least-privilege builder.

## 4. Resulting host (before → after)

- **Before:** four ceremony files ≈ 400 hand-written lines.
- **After:** `GameWorldAccess` (domain, unchanged) + a generated connection host + a handful of
  `InstallAndWireAsync` / `Wire` calls. The control service is ~40 lines of domain + lifecycle.

The server ends up reading like the plugin already does.

## 5. Trust-boundary invariants (unchanged)

- Routing/effects/index-coverage/terminal-kind recomputed from verified IR; manifest never trusted.
- Verified `ShouldHandle` always runs (index is prefilter only).
- Ownership and policy stay host-owned (`OwnerId`, `GetRequiredCapabilities`, the `policy`/`validate` seams).
- The helpers are the **single audited home** for these, replacing per-host copies — a net security gain.

## 6. New public surface (summary — as shipped)

- `PluginEventAdapterRegistry.TryResolveErased(string, out IErasedPluginEventAdapter)`
- `IErasedPluginEventAdapter` (EventType + captured generic wire dispatch) + `WireCallbacks`
- `PluginServer.WireHook(InstalledKernel, WireOptions?)` / `WireSubscription(...)` + `WireOptions`
- `KernelWireKind` / `KernelWireTerminal` (trusted classification)
- `PluginSession.InstallAndWireAsync(PluginPackage, Action<InstalledKernel> wire, ...)` and `PluginSession.TryGetOwned(...)`
- `InstalledKernel.InvokeServerExtensionRpcAsync(byte[], CancellationToken)`
- `PluginConnectionHost<TConnection>` in `DotBoxD.Pushdown.Services` — the runtime connection host (Piece 2 shipped as a helper, not a source generator)

## 7. Sequencing & risk

1. **Piece 1 (router)** — biggest win, runtime-only (lowest risk). Validate by deleting
   `GamePluginKernelWiring` in the sample and adding a **router-parity test**: assert `server.Wire` routes
   identically to the hand-written wiring for every terminal kind (Plain / Projecting / Result /
   ProjectingResult, hook + subscription, indexed + broad).
2. **Piece 4 (InstallAndWire)** — depends on Piece 1.
3. **Piece 3 (invoker)** — independent, small.
4. **Piece 2 (generated host)** — biggest codegen change; spike separately (same Roslyn constraints as #88).

Each phase keeps the existing trust-boundary tests green.

## 8. Open questions

- `IErasedPluginEventAdapter`: public (hosts registering custom adapters may want it) or internal?
- Does `Wire` live on `PluginServer` (raw wire) or `PluginSession` (ownership context)? Likely both: `Wire`
  on the server, `InstallAndWireAsync` on the session.
- Generated host: how to express the multi-service factory ergonomically while reusing the `Provide*`/`Get*`
  generated names.
- Index routing: default-on (proposed) vs opt-in via `WireOptions.UseIndex`.
- Should `KernelWireTerminal` be public (host inspection / custom routing) or internal?
