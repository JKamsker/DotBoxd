# Kernels (validated sandbox logic)

A **Kernel** is client/plugin-supplied logic the host runs **safely under policy**. It is restricted
**IR** (authored as JSON), never C#, IL, reflection, CLR member names, or arbitrary host calls.

Lifecycle (via `SandboxHost` in `DotBoxD.Hosting`):

1. **Import** — parse JSON IR into a `SandboxModule`; reject anything outside the allowed shape.
2. **Validate** — structural, type, effect, capability, and binding checks (`DotBoxD.Kernels.Validation`).
3. **Prepare** — produce a sealed `ExecutionPlan`.
4. **Execute** — run on one of two backends:
   - **Interpreter** (`DotBoxD.Kernels.Interpreter`) — flexible, async-friendly.
   - **Compiler** (`DotBoxD.Kernels.Compiler`) — emits verified IL; the generated assembly is checked by
     `DotBoxD.Kernels.Verifier` before it runs. Compiled async bindings run through a trusted runtime
     trampoline; generated kernel IL stays synchronous.

Everything is **metered**: fuel, loop iterations, call depth, list length, output bytes, and
per-capability quotas. A buggy or hostile kernel cannot exhaust host resources or reach disallowed
effects. Host capabilities (files, time, random, logging, HTTP via `DotBoxD.Hosting.Http`) are exposed
only through explicit **capability grants** — see [capabilities-and-bindings](../security/sandbox-caveats.md)
and the full spec under [`docs/Specs/`](../Specs/).

Async binding tails are also capability-gated. Policies must grant `dotboxd.runtime.async` via
`AllowRuntimeAsync()` before kernels can call bindings that may complete asynchronously.

> **Important:** the kernel sandbox is the real boundary. It is **not** the same as loading a .NET
> plugin assembly — see [security/sandbox-caveats.md](../security/sandbox-caveats.md).

Diagnostics use the `DBXK###` prefix. **See also:** the GameServer sample under
[`samples/GameServer`](../../samples/GameServer), [runtime](runtime.md), and
[`docs/examples/coverage-gaps.md`](../examples/coverage-gaps.md) for kernel scenarios no longer shown
by maintained samples.
