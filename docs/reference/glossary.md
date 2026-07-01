# Glossary

Short, plain-language definitions of the core DotBoxD terms, each linking to the page that covers it
in depth. If you landed mid-tree — say on [Pushdown](../concepts/pushdown.md) or
[Kernel runtime](../concepts/runtime.md) — look the unfamiliar words up here first, then follow the
link for the full treatment.

## Sandbox and kernels

- **Kernel** — Client/plugin-supplied logic the host runs safely under policy, as
  [validated, capability-gated, fuel-metered IR](../concepts/kernels.md) — never C#, IL, or reflection.
- **IR (intermediate representation)** — The restricted, JSON-authored instruction format a
  [kernel](../concepts/kernels.md) is expressed in; the host rejects anything outside the allowed shape
  before it runs.
- **Lowering** — Compile-time rewriting of authored C# (a `.Where`/`.Select` chain or a
  `[ServerExtension]` batch) into [verified IR that runs server-side](../tutorials/event-pipeline-runlocal.md).
- **Host binding** — A [`[HostBinding]`](../concepts/kernels.md) method the host explicitly exposes;
  the only way a kernel reaches outside pure computation, and only when the matching capability is granted.
- **Capability** — A named grant (e.g. `file.read`) the [host policy must give](../security/sandbox-caveats.md)
  before a kernel may use the matching effect; derived from the IR the kernel actually touches, and
  fail-closed.
- **Effect (`SandboxEffect`)** — The category of outside-world impact an operation has (`Cpu`, `Alloc`,
  file/network/host effects, `Time`, `Random`, `Concurrency`, `Audit`), [controlled by the policy](../concepts/runtime.md).
- **Fuel and metering (quota)** — Fuel is an abstract instruction budget; [metering](../concepts/runtime.md)
  charges every operation and enforces loop, call-depth, list-length, output, and per-capability quotas,
  stopping a kernel that runs over.
- **`SandboxPolicy`** — The immutable [hard budget](../concepts/runtime.md) every kernel run is bounded by:
  fuel, loop/depth/output limits, capability grants, and effect controls.
- **Manifest** — The public artifact declaring a [kernel's required capabilities](../concepts/kernels.md)
  (the union of what its IR touches); install fails closed if the host policy does not grant them.
- **Trust boundary** — The line that actually contains untrusted code: validated
  [kernel IR is one](../security/sandbox-caveats.md); loading a .NET assembly (`AssemblyLoadContext`) is not.

## Modes and authoring

- **Pushdown** — Collapsing many small remote calls into [one validated server-side batch](../concepts/pushdown.md)
  that loops the host's existing bindings next to its data.
- **Server extension** — A plugin's [`[ServerExtension]`](../concepts/pushdown.md) batch aggregate,
  lowered to a sandboxed kernel and installed into a frozen host without recompiling it.
- **Hook / Subscription** — The two [event registries](../tutorials/event-pipeline-runlocal.md) a plugin
  attaches reactions to: `server.Hooks` are awaited decision points whose logic can influence the outcome;
  `server.Subscriptions` are fire-and-forget notifications.
- **Event-pipeline terminals (`RunLocal` / `Run` / `RegisterLocal` / `Register` / `Use`)** — The last call in an
  [event pipeline](../concepts/event-pipelines.md#the-terminals-run-modes), chosen on two axes:
  *where your handler runs* — in your plugin as native C# (`RunLocal`, `RegisterLocal`) or server-side as
  sandboxed IR (`Run`, `Register`) — and *whether it returns a decision* — `Register` / `RegisterLocal` hand an
  `IHookResult` back to the server, while `Run` / `RunLocal` are fire-and-forget. `Use<TKernel>` installs a
  separately-authored kernel. Result terminals (`Register` / `RegisterLocal`) exist only on `server.Hooks`
  (awaited decisions), never on fire-and-forget `server.Subscriptions`.

## Services, RPC, and transport

- **RPC** — Remote procedure call: a discrete, typed request→response to a host capability behind a
  shared C# [`[DotBoxDService]` contract](../concepts/services.md).
- **Peer** — An [`RpcPeer`/`RpcHost`](../concepts/services.md) endpoint; the runtime is peer-based and
  bidirectional, so one connection can both serve and call services.
- **Proxy / dispatcher** — The [generated](../concepts/services.md) client-side stub (proxy) that marshals
  a call over the wire and the server-side dispatcher that routes it to your implementation.
- **IPC (inter-process communication)** — Two OS processes (host and plugin/client) talking over a
  transport such as a named pipe or TCP.
- **DTO (data transfer object)** — A plain data type that crosses the wire; MessagePack DTOs are
  annotated with `[MessagePackObject]` and a stable `[Key]` per member — see [Services](../concepts/services.md).
