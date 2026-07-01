# Sandbox caveats — what is and isn't a boundary

> **The single most important security fact about DotBoxD:**
> **`AssemblyLoadContext` is not a sandbox.** Loading a .NET assembly — even into a separate
> `AssemblyLoadContext` — gives that code the full permissions of the process. It is an *isolation*
> mechanism for assembly versioning, **not** a security boundary.

DotBoxD has three execution modes. They look superficially similar ("run some logic the caller
supplied") but have completely different security guarantees. Choose the mode that matches how much you
trust the code.

## 1. Safe mode (Kernels) — the real boundary

A **kernel** is restricted **IR** (intermediate representation), authored as JSON, not C#/IL. The host:

1. **Imports** the IR and rejects anything outside the allowed shape (no reflection, no CLR member
   names, no arbitrary host calls, no unbounded constructs).
2. **Validates** it against a policy: structural, type, effect, capability, and binding checks.
3. **Meters** execution: fuel, loop-iteration, call-depth, list-length, and output budgets, plus
   per-capability quotas. A buggy or hostile kernel cannot run away with host resources.
4. For **compiled** mode, **verifies** the generated assembly before it runs (the verifier enforces the
   same restrictions the interpreter does).

Async host bindings remain part of the same boundary. They are disabled by default, add the
`Concurrency` effect, and require the host policy to grant `dotboxd.runtime.async`. A binding that
returns a genuinely pending `ValueTask` without that grant is rejected instead of being blocked on.

This is the boundary DotBoxD is built to defend, and it is exercised by a required security-boundary
test suite on every CI run. It defends against author-supplied *logic expressed as IR* — both buggy and
many malicious authors.

## 2. Trusted-plugin mode — NOT a security boundary

Loading a normal .NET plugin assembly (via `AssemblyLoadContext`) runs real CLR code with full process
permissions. Use it only for **code you already trust** (first-party plugins, vetted partners). Do not
treat `AssemblyLoadContext` unload/isolation as containment of untrusted behavior.

## 3. Untrusted arbitrary .NET code — requires an OS boundary

If you must run third-party **compiled** code you do not trust, put a real boundary around it:

- a separate **worker process** with reduced privileges,
- a **container** / VM, or
- an OS-level isolation mechanism (job objects, seccomp, AppContainer, etc.).

In-process restrictions are not a substitute for an OS/hypervisor boundary for this case.

## Why safe mode is the boundary — and why capabilities + metering

The three modes are superficially symmetric — each "runs some logic the caller supplied" — so the
load-bearing decision is **trust level, not convenience**. Only validated IR is a trust boundary: its
guarantees are established *before* the logic runs and *bounded* while it runs. A loaded assembly's
guarantees are established *never*.

**The alternative it beats.** Running author-supplied logic as C#/IL — or loading it as a plugin
assembly — hands it the full CLR (filesystem, sockets, P/Invoke, spawning processes) regardless of any
DotBoxD API wrapped around it. The kernel replaces that with restricted IR the host inspects and rejects
*before* execution, so a method reachable via normal RPC is **not** automatically reachable from a
kernel (see [Kernels](../concepts/kernels.md)). That substrate is what makes accepting untrusted author
logic safe in-process.

**Why capabilities (least privilege).** A kernel starts with *no* ambient authority: every host
operation it can reach — files, time, random, logging, HTTP — must be an explicit `[HostBinding]` the
host exposed and a capability the policy granted. In the
[`README`](https://github.com/JKamsker/DotBoxD/blob/main/README.md) example, `Kill` is pinned to
capability `"game.world.monster.write.kill"` with effects `SandboxEffect.Cpu |
SandboxEffect.HostStateWrite`; nothing outside that enumerated surface is reachable. Grants are
**derived and fail closed** — the required set is the union of what the IR actually touches, and install
is rejected unless the policy grants them, so bad code never runs. The reachable blast *surface* is what
the host enumerates, not what the author discovers.

**Why metering (bounded blast radius).** Capabilities bound *what* a kernel may touch; metering bounds
*how much*. The policy is a hard budget — `WithFuel`, `WithMaxLoopIterations`, `WithMaxListLength`,
per-capability quotas — and execution reports `ResourceUsage.FuelUsed` back to the host. Together they
cap the blast radius even for *granted* operations: HTTP cannot become unbounded requests, a loop cannot
spin forever, output cannot exhaust the output budget. Least privilege plus bounded consumption is why a
*hostile* author, not just a *buggy* one, can be accepted in this mode.

**Why `AssemblyLoadContext` isn't a boundary.** ALC provides assembly-versioning and unload isolation,
not containment — it never inspects, restricts, or meters what the code does. So the two modes that look
symmetric are not: a validated kernel can only do what the policy permits; a loaded assembly can do
anything the process can. Conflating them is the exact mistake this page exists to prevent, which is why
the [trust posture](https://github.com/JKamsker/DotBoxD/blob/main/SECURITY.md) is stated as a table, not
a nuance.

**Pushdown inherits this boundary — it does not widen it.** A plugin's server-side batch (Pushdown /
server extensions) lowers to the *same* validated, capability-gated, fuel-metered IR as an event kernel
and reaches only bindings the host already exposes. Moving the loop next to the data collapses N
round-trips into one server-side batch **without** relaxing the trust model or recompiling the frozen
host.

**When to reach for each.** Untrusted author *logic* → **safe mode (Kernels)**. Logic that is a host
capability *you* implement and trust → a plain [Service](../concepts/services.md) RPC, which needs no
sandbox. Already-trusted first-party or vetted-partner *assemblies* → trusted plugin. Untrusted
*compiled* .NET → an OS/process boundary (mode 3 above). One honest limit: the kernel boundary defends
against buggy and *many* malicious authors — deliberately "many," not "all" — so for hard multi-tenant
isolation of hostile compiled code, do not lean on safe mode.

## Summary

| Mode | Input | Boundary | Use for |
|------|-------|----------|---------|
| Safe (Kernels) | validated restricted IR | **Yes** (validation + metering + verification) | untrusted author logic |
| Trusted plugin | .NET assembly | **No** | code you already trust |
| Untrusted assembly | .NET assembly | **OS-level only** | requires process/container/VM isolation |

See [`SECURITY.md`](../../SECURITY.md) for reporting, and the full threat model under
[`docs/Specs/`](https://github.com/JKamsker/DotBoxD/tree/main/docs/Specs).
