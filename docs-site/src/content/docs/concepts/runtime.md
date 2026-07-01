---
title: 'Kernel runtime'
description: 'The kernel runtime executes validated IR under hard budgets. Key pieces:'
---
The kernel runtime executes validated IR under hard budgets. Key pieces:

## Why two backends, and why everything is metered

A kernel is untrusted, author-supplied logic (restricted IR, never C#/IL/reflection) that the host runs
**in-process** — there is no OS process boundary by default, so the runtime *is* the boundary (see
[kernels.md](/concepts/kernels/)). That single fact drives every design choice on this page:

- **Resource containment** — a buggy or hostile kernel must not be able to exhaust host CPU, memory,
  I/O, or output size. This is why everything is metered.
- **Effect containment** — a kernel can only touch the outside world through host-granted capabilities.
  This is why bindings and capability grants exist.

Backend selection is therefore a **performance decision, never a safety decision**: both backends must
enforce identical guarantees, and the interpreter defines what "correct" means.

### Why an interpreter *and* a compiler

- The **interpreter** is the default and the safety baseline. It walks verified IR directly, emits no
  code, and so adds no new attack surface. Metering is just method calls the evaluator makes as it walks
  nodes, so quotas and diagnostics are trivial, and it is a normal managed method that can `await` a
  pending host binding mid-execution — which is why async bindings always run interpreted.
- The **compiler** exists purely for throughput: interpretation pays per-node dispatch overhead on
  every run, so a hot kernel executed thousands of times amortizes compilation into near-native speed.
  It emits real IL (via `PersistedAssemblyBuilder`) and caches the artifact, content-addressed by
  module hash + entrypoint + policy hash + compiler version.

But emitting IL reintroduces exactly the attack surface the interpreter avoids — arbitrary IL could
box/unbox, reach static state, forge references, call forbidden members, throw, or skip metering. That
is why the compiled path is only safe because of the **Verifier** (`DotBoxD.Kernels.Verifier`):

- Emitted assemblies are verified **before they ever run**, and compilation throws if verification
  fails
  ([`ReflectionEmitSandboxCompiler.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Kernels/DotBoxD.Kernels.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs)).
  **Cache reads are re-verified too**, so a tampered on-disk artifact cannot smuggle unverified IL into
  the process.
- Verification enforces an **opcode allowlist** with an explicit forbidden set — `Calli`, `Jmp`,
  `Localloc`, `Cpblk/Initblk`, `Ldftn/Ldvirtftn`, `Ldtoken`, `Box/Unbox`, `Castclass/Isinst`,
  `Ldsfld/Stsfld`, `Throw/Rethrow`, `Starg`, `Arglist` — i.e. every primitive that could break type
  safety, forge references, reach static state, or escape the ABI; exception handlers are rejected
  outright
  ([`OpCodeVerifier.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Kernels/DotBoxD.Kernels.Verifier/OpCodeVerifier.cs)).
- It enforces a **member allowlist**
  ([`VerificationPolicy.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Kernels/DotBoxD.Kernels.Verifier/VerificationPolicy.cs))
  restricted to corelib primitives plus the metering/value facade, with allowed assemblies pinned by
  strong identity (version + public key token).

This is what lets the compiled path "enforce the same restrictions as the interpreter": the interpreter
is safe by construction, and the compiler is safe because verification proves the emitted IL can *only*
do what the interpreter would.

### Why compiled code is still metered

Metering is not skipped just because the code is native IL — the compiler **emits the charge calls into
the IL itself**. The verifier's member allowlist includes exactly the metering ABI (`ChargeFuel`,
`ChargeLoopIteration`, `ChargeBindingCall`, `EnterCall`/`ExitCall`, …) on
[`CompiledRuntime`](https://github.com/JKamsker/DotBoxD/blob/main/src/Kernels/DotBoxD.Kernels.Runtime/CompiledRuntime.cs),
which forwards to the same meter the interpreter uses. Both backends build a fresh `ResourceMeter` per
run against the same context, so fuel and quota semantics are identical by construction. (`CompiledRuntime`
is marked `EditorBrowsable(Never)`: it is generated-code ABI kept in lockstep with the verifier
allowlist, not host API.) The same guarantee extends to [pushdown](/concepts/pushdown/) server extensions, which
run as the same validated, metered, capability-gated kernels.

### Why fuel *and* a wall-clock deadline

All limits live in one immutable
[`ResourceLimits`](https://github.com/JKamsker/DotBoxD/blob/main/src/Kernels/DotBoxD.Kernels/Model/ResourceLimits.cs)
record with fail-safe defaults: an instruction (`MaxFuel`) budget, loop-iteration and call-depth
budgets, `MaxHostCalls` plus per-capability quotas, collection-cardinality caps, `MaxAllocatedBytes`,
file/network byte caps (write default `0` — fail-closed), log/output caps, and a wall-time deadline.
Fuel is charged because it is **deterministic and reproducible** — independent of machine speed or GC
pauses — so quota exhaustion is testable and identical across runs; the wall-clock deadline is a cheap
secondary backstop, checked only every N charges to keep the hot path fast
([`ResourceMeter.HostCalls.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Kernels/DotBoxD.Kernels/Model/ResourceMeter.HostCalls.cs)).
Every charge is overflow-checked and throws `QuotaExceeded` on breach, and the meter is created fresh
per run, so a budget can neither wrap around nor leak across executions.

### When to pick which backend

Backend is chosen per run via `SandboxExecutionOptions.Mode` (`Interpreted` / `Compiled` / `Auto`); a
host opts into compilation with `UseCompilerIfAvailable(...)` + `UseCompilerCache(...)` on top of the
always-available `UseInterpreter(...)`
([`SandboxHostBuilder.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Hosting/DotBoxD.Hosting/Execution/Host/SandboxHostBuilder.cs)).

- **Interpreter** — the default. Reach for it for the correctness baseline, debugging (`EnableDebugTrace`
  forces it), one-shot/cold kernels, AOT targets, and **anything with async host bindings** (generated
  kernel IL stays synchronous, so async bindings always fall back to the interpreter).
- **Compiler** — hot paths where the same plan runs repeatedly and compile cost amortizes; requires a
  verifiable artifact and a compilable entrypoint.
- **Auto** (recommended) — starts interpreted and promotes to compiled by *hotness*: the first run is
  always interpreted, and the selector promotes once `RunCount` crosses `max(2, AutoCompileThreshold)`,
  falling back to the interpreter whenever the compiler is unavailable, debug tracing is on, or the
  entrypoint is async/uncompilable
  ([`SandboxHost.Auto.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Hosting/DotBoxD.Hosting/Execution/Host/SandboxHost.Auto.cs),
  [`ExecutionModeSelector.cs`](https://github.com/JKamsker/DotBoxD/blob/main/src/Hosting/DotBoxD.Hosting/Execution/ExecutionModeSelector.cs)).
  You pay compilation cost only for kernels that are actually hot, while every kernel still runs
  immediately on the safe baseline.

## Reference: backends & metering

The two sections above explain the *why*; this is the canonical quick reference. Both backends enforce
identical guarantees — pick by performance, not safety.

| Backend | Assembly | Role |
| --- | --- | --- |
| **Interpreter** | `DotBoxD.Kernels.Interpreter` | Default and safety baseline: walks verified IR directly, no codegen, AOT-friendly. |
| **Compiler** | `DotBoxD.Kernels.Compiler` | Emits verified IL for hot kernels; the artifact is checked by `DotBoxD.Kernels.Verifier` before every run. |

Every run is bounded by a `SandboxPolicy`:

- **fuel** (instruction budget), **loop iteration** and **call-depth** budgets,
- **list/collection cardinality** and **output-byte** budgets,
- **capability grants** (e.g. `file.read`, `net.http.get`) with parameters, expiry, and per-capability
  quotas,
- **effect** controls (`Cpu`, `Alloc`, file/network/host effects, `Time`, `Random`, `Concurrency`,
  `Audit`), with a deterministic mode (logical clock + seeded random) available.

## Effects & capabilities

Bindings (`DotBoxD.Kernels.Runtime`, `DotBoxD.Hosting.Http`) are the only way a kernel reaches outside
pure computation, and only when the policy grants the matching capability. This is what makes
author-supplied logic safe to run in-process. See
[security/sandbox-caveats.md](/security/sandbox-caveats/) and the full specification under
[`docs/Specs/`](https://github.com/JKamsker/DotBoxD/tree/main/docs/Specs).

Async-capable bindings are opt-in. A binding marked `BindingDescriptor.IsAsync` adds the
`Concurrency` effect and requires the `dotboxd.runtime.async` runtime capability. Hosts grant it with
`SandboxPolicyBuilder.AllowRuntimeAsync()`; without that grant, preparation fails closed and the
runtime backstop rejects genuinely pending `ValueTask` results.

When a plugin authoring interface uses `[HostBinding]`, set the additive
`HostBindingAttribute.IsAsync` named property to mirror the registered descriptor's `IsAsync` value.
The property defaults to `false`, so existing source remains compatible while async host bindings can
derive `dotboxd.runtime.async` into generated manifests.

## Next

See these runtime guarantees end-to-end in the [GameServer walkthrough](/examples/gameserver-walkthrough/).
