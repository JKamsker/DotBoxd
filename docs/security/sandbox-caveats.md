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

## Summary

| Mode | Input | Boundary | Use for |
|------|-------|----------|---------|
| Safe (Kernels) | validated restricted IR | **Yes** (validation + metering + verification) | untrusted author logic |
| Trusted plugin | .NET assembly | **No** | code you already trust |
| Untrusted assembly | .NET assembly | **OS-level only** | requires process/container/VM isolation |

See [`SECURITY.md`](../../SECURITY.md) for reporting, and the full threat model under
[`docs/Specs/`](../Specs/).
