# Security Policy

DotBoxD is positioned as a **safe extension runtime**, so we hold its trust boundary to a high
standard and document it precisely. Please read the boundary model below before deploying.

## Reporting a vulnerability

Please report suspected vulnerabilities **privately** — do not open a public issue for a security
problem.

- Use GitHub's **"Report a vulnerability"** (Security → Advisories) on this repository, or
- email the maintainer (see the GitHub profile of `@JKamsker`).

Include a description, affected package(s)/version(s), and a minimal reproduction if possible. We aim
to acknowledge within a few days and will coordinate a fix and disclosure timeline with you.

## Supported versions

DotBoxD is pre-1.0 (`0.x`). Only the latest published version receives security fixes while the API
stabilizes.

## The trust boundary — what is and isn't a sandbox

DotBoxD has three distinct execution modes with very different guarantees. Treat them as different
security postures:

| Mode | What runs | Boundary? |
|------|-----------|-----------|
| **Safe mode (Kernels)** | Validated, capability-gated, fuel/quota-metered restricted **IR** (never C#, IL, reflection, CLR member names, or arbitrary host calls). Compiled kernels are additionally verified before execution. | **Yes** — this is the real in-process boundary DotBoxD is built to defend. |
| **Trusted-plugin mode** | Normal .NET assemblies loaded via `AssemblyLoadContext`. | **No.** `AssemblyLoadContext` is *not* a security sandbox — loaded code runs with the full permissions of the process. Use only for code you already trust. |
| **Untrusted arbitrary .NET code** | Any third-party assembly you do not trust. | Requires an **OS-level** boundary — a separate worker process, container, VM, or equivalent. In-process restrictions are not sufficient. |

The kernel sandbox defends against runaway resource use, disallowed effects, and disallowed host
access for *author-supplied logic expressed as IR*. It does **not** turn `AssemblyLoadContext` into a
sandbox and does **not** make loading untrusted compiled assemblies safe.

For the full threat model, capability/binding model, and the verifier's guarantees, see
[`docs/security/`](docs/security/sandbox-caveats.md) and the kernel sandbox specification under
[`docs/Specs/`](https://github.com/JKamsker/DotBoxD/tree/main/docs/Specs).
