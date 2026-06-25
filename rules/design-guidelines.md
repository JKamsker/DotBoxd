# DotBoxD Design Guidelines

Binding design rules for the DotBoxD libraries. They apply to all new public surface, abstractions,
and source generators, and they take precedence over convenience. Referenced from
[`AGENTS.md`](../AGENTS.md) and [`CLAUDE.md`](../CLAUDE.md).

## Design principles

DotBoxD's public surface aims to be **Simple, Obvious, Discoverable, Consistent, Minimal, and
Composable**. When a convenience would compromise one of these, prefer the smaller, more explicit
surface.

## Primary rule: abstractions are opt-in sugar, never lock-in

The library must be usable **any way the consumer wants — including ways we did not foresee.** Every
high-level convenience (a helper, a builder, an attribute-driven source generator) is **opt-in sugar
layered over public primitives.** It must never be the *only* way to do something, and it must never
prevent a usage we didn't anticipate. If a convenience does get in someone's way, they must be free to
hand-write the boilerplate themselves, and we must not disallow it.

**The litmus test.** For any generator or high-level abstraction:

> *Can the consumer delete the attribute / stop calling the helper and hand-write the exact same thing
> using only public API?*

If the answer is ever **no**, it is lock-in — redesign it.

### What this requires

1. **Layered, independently usable surface.** Raw primitives → convenience helpers → optional
   generator. Each layer is usable on its own; a higher layer only ever calls the *public* API of a
   lower one and never gates access to it. The consumer may stop at any layer.
   - Canonical example (host-side plugin server, see
     [host composition layer](../docs/design/host-composition-layer/minimal-host-setup.md)):
     `session.InstallAsync` / `server.Hooks.On<T>().Use(kernel)` / `RpcMessagePackIpc.ListenNamedPipe`
     / `peer.Provide<T>` (primitives) → `InstallAndWireAsync` / `WireHook` / `WireSubscription` /
     `InvokeServerExtensionRpcAsync` / `PluginConnectionHost` (helpers) → a future
     `[GeneratePluginServerHost]` (optional generator).

2. **Generators emit only public-API calls.** A generator must never reach an `internal` member the
   consumer could not call. Anything it generates must be reproducible by hand. This single rule
   defeats most lock-in.

3. **Generators implement a public contract the consumer could implement by hand.** No
   generated-only interface or type that can be satisfied *only* by the generator.

4. **Granular opt-out — never one all-or-nothing switch.** The convenience attribute (e.g.
   `[GeneratePluginServer(Context = typeof(GameContext))]`) is an *all-on* default: it generates the
   full set of required code, as it does today. But every facet it produces must be **independently
   disable-able**, so a consumer can keep the default *minus* the one part they want to own — without
   giving up the rest.
   - **Explicit per-facet opt-out / opt-in.** The attribute exposes a way to turn off (and back on) any
     individual output it enables — e.g. a `Generate{Facet} = false` flag or an `Exclude` set — not a
     single on/off switch for the whole thing.
   - **Implicit, user-wins opt-out.** The marker sits on a `partial` type; if the consumer hand-wrote a
     member or type the generator would emit, the generator detects it and skips *just that one*.
   - No attribute ⇒ no generation. And taking the default must never force taking *all* of it.

5. **Common case only.** A generator handles the common shape; the moment a consumer is outside it (a
   different transport, an extra service, a custom install ordering, multiple connections, …) they
   drop to the helpers for that part. The helpers stay directly callable, so an unforeseen pattern is
   never *blocked* — it simply doesn't get the sugar.

### Guarding it

If a generator is built, add a test that **generates one path and hand-writes the equivalent the
other way, then asserts identical behavior** — so "delete-the-attribute-and-hand-write-it" stays
provably true and cannot silently rot.

### Anti-patterns (do not ship)

- All-or-nothing buy-in — the consumer must adopt the whole abstraction or none of it.
- A generator or helper that calls `internal` members the consumer cannot reach.
- Baked-in assumptions (a fixed service set, a single connection, one transport, a method-naming
  convention) that preclude a shape the consumer might reasonably want.
