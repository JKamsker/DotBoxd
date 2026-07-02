---
title: 'Kernels (validated sandbox logic)'
description: 'A Kernel is client/plugin-supplied logic the host runs safely under policy. It is restricted IR (intermediate representation), authored as JSON, never C#,…'
---
A **Kernel** is client/plugin-supplied logic the host runs **safely under policy**. It is restricted
**IR** (intermediate representation), authored as JSON, never C#, IL, reflection, CLR member names,
or arbitrary host calls.

## Why Kernels (the sandbox behind Query filters and Pushdown batches)?

**The problem.** Two of DotBoxD's three modes run *code the plugin author wrote* inside the host
process: the [Query pipeline](/tutorials/event-pipeline-runlocal/) lowers `Where`/`Select` to
server-side logic, and [Pushdown](/concepts/pushdown/) lowers a batch method that loops the host's bindings
server-side. Running that logic as C#/IL would hand it full CLR access. The alternative — filtering
client-side or bloating a frozen host with every batch op — either wastes the wire or is impossible.
The kernel is the shared substrate that makes accepting untrusted author logic safe.

**The payoff.** A kernel is validated restricted IR, capability-gated, and metered: fuel (fuel is an
abstract instruction budget — each operation costs fuel, and the kernel is stopped when the budget
runs out), loop iterations, call depth, list length, output bytes, and per-capability quotas. A buggy or hostile
kernel cannot exhaust host resources or reach disallowed effects, and it can touch only the
[host bindings](/concepts/host-bindings/) the host explicitly exposes — a method reachable via
normal RPC is **not** automatically reachable from a kernel. This is what makes both server-side
lowerings safe to accept from less-trusted plugin authors.

**Grounded specifics:**

- **One boundary, two lowerings.** Query's `Where`/`Select` and Pushdown's batch run as the *same*
  validated, fuel-metered IR. Query keeps the payoff on the wire — the filter runs server-side so
  non-matching events never cross, and `Select` ships only the projected scalar (fewer bytes, fewer
  wake-ups, one-way push, no round-trips). Pushdown keeps it in round-trips — N fine-grained calls
  collapse into **one** server-side batch next to the host's data.
- **Capabilities are derived, not self-asserted, and fail closed.** The manifest's required
  capabilities are the union of what the IR actually touches; install is rejected unless the host
  policy grants them, so bad code never runs. Async is itself a gated capability
  (`dotboxd.runtime.async`) that fails closed without a grant.
- **No lock-in.** The IR plus manifest are public artifacts; you can delete the `[ServerExtension]`
  attribute and hand-author the same IR through the same `SandboxHost` pipeline. The generator emits
  nothing a hand-written kernel could not also request.

**When to use it — and when not.** Reach for kernels (directly, or via Query/Pushdown lowering) when
you must run author-supplied logic in-process safely. Prefer a [Service](/concepts/services/) when the logic
is a host capability *you* implement and trust — it is a plain request/response RPC that needs no
sandbox. And note the trust ceiling below: safe-mode kernels are the real boundary; trusted-plugin
assembly loading is **not** a sandbox, and hard multi-tenant isolation against arbitrary compiled
.NET still needs an OS/process boundary.

Lifecycle (via `SandboxHost` in `DotBoxD.Hosting`):

1. **Import** — parse JSON IR into a `SandboxModule`; reject anything outside the allowed shape.
2. **Validate** — structural, type, effect, capability, and binding checks (`DotBoxD.Kernels.Validation`).
3. **Prepare** — produce a sealed `ExecutionPlan`.
4. **Execute** — run on one of two backends:
   - **Interpreter** (`DotBoxD.Kernels.Interpreter`) — flexible, async-friendly.
   - **Compiler** (`DotBoxD.Kernels.Compiler`) — emits verified IL; the generated assembly is checked by
     `DotBoxD.Kernels.Verifier` before it runs. Compiled async bindings run through a trusted runtime
     trampoline; generated kernel IL stays synchronous.

For the smallest end-to-end host, see section 2 (Kernels) of the root README
(https://github.com/JKamsker/DotBoxD/blob/main/README.md).

Beyond that metering, host capabilities (files, time,
random, logging, HTTP via `DotBoxD.Hosting.Http`) are exposed
only through explicit [host bindings](/concepts/host-bindings/) and **capability grants** — see
[capabilities-and-bindings](/security/sandbox-caveats/)
and the full spec under [`docs/Specs/`](https://github.com/JKamsker/DotBoxD/tree/main/docs/Specs).

Async binding tails are also capability-gated. Policies must grant `dotboxd.runtime.async` via
`AllowRuntimeAsync()` before kernels can call bindings that may complete asynchronously.

> **Important:** the kernel sandbox is the real boundary. It is **not** the same as loading a .NET
> plugin assembly — see [security/sandbox-caveats.md](/security/sandbox-caveats/).

Diagnostics use the `DBXK###` prefix. **See also:** the [GameServer walkthrough](/examples/gameserver-walkthrough/)
for a beginner-friendly tour, the GameServer sample under
[`samples/GameServer`](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer), [runtime](/concepts/runtime/), and
[`docs/examples/coverage-gaps.md`](/examples/coverage-gaps/) for kernel scenarios no longer shown
by maintained samples.
