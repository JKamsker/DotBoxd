# Safe IR Sandbox Specification

This specification describes a C#/.NET sandbox where untrusted users submit a restricted JSON intermediate representation (JSON IR), not C# and not MSIL. The host validates the IR, grants explicit capabilities, executes it either by walking the IR in an interpreter or by compiling it to a compiled runtime artifact, and ensures all dangerous operations are routed through safe host APIs.

The core principle is simple:

```text
Untrusted user input is JSON IR only.
The user never supplies .NET IL, assemblies, raw method tokens, type names, delegates, reflection objects, or arbitrary API references.
```

The sandbox has two execution modes:

1. **Interpreted mode** for quick, rare, low-volume, debuggable execution by walking the verified IR directly, without generating IL, a `DynamicMethod`, or a DLL.
2. **Compiled mode** for hot paths, where verified IR is emitted into a compiler-owned runtime form such as a `DynamicMethod` or a valid .NET assembly, verified/gated, loaded or delegated, cached where applicable, and executed.

Both modes share the same IR, type system, effect system, binding registry, capability policy, resource accounting, audit model, and tests.

## Important security position

This project does not rely on `AssemblyLoadContext`, `AppDomain`, Code Access Security, or API blacklists as a security boundary. `AssemblyLoadContext` is useful for loading/version isolation, and collectible contexts can help unload generated assemblies, but it is not a security sandbox. Modern .NET code in the same process runs with the process permissions.

The security boundary is:

```text
restricted IR + type/effect validation + host-granted bindings + generated-code verifier + optional OS process boundary
```

For hard isolation against runtime bugs, memory exhaustion, process termination, hostile native behavior, or unknown escapes, run the sandbox execution worker in a separate process/container/restricted OS account.

## Document map

| File | Purpose |
|---|---|
| `spec/00-overview.md` | Product overview and mental model |
| `spec/01-goals-non-goals.md` | Scope, goals, non-goals |
| `spec/02-threat-model.md` | Attackers, assets, assumptions, security invariants |
| `spec/03-architecture.md` | Components and end-to-end pipeline |
| `spec/04-ir-language.md` | IR shape, operations, validation rules |
| `spec/05-type-system.md` | Allowed types, forbidden types, value model |
| `spec/06-effects-capabilities.md` | Effects, policy, capability grants |
| `spec/07-bindings.md` | Host API binding model and manifests |
| `spec/08-runtime-safe-apis.md` | Safe facades for IO, network, game state, time, random |
| `spec/09-interpreted-mode.md` | Interpreter backend, quick execution, debugging, hotness transition |
| `spec/10-compiled-mode.md` | IR to compiled runtime artifact backend, caching, loading |
| `spec/11-generated-code-verifier.md` | Post-emit verifier requirements |
| `spec/12-resource-limits.md` | Fuel, time, memory, IO quotas, process boundary |
| `spec/13-cache-versioning.md` | Cache keys, invalidation, manifests, policy changes |
| `spec/14-observability-audit.md` | Logs, metrics, traces, audit records |
| `spec/15-testing-red-team.md` | Required tests and malicious fixtures |
| `spec/16-public-api.md` | Current C# API surface |
| `spec/17-implementation-plan.md` | Build order and acceptance criteria |
| `../Addendum/Addendum.md` | Plugin/kernel addendum specification |
| `../Addendum/Examples.md` | Plugin/kernel addendum runnable examples |
| `adr/0001-restricted-ir-not-csharp.md` | Decision: no arbitrary C# |
| `adr/0002-two-execution-backends.md` | Decision: interpreter + compiler |
| `adr/0003-host-granted-bindings.md` | Decision: only host grants bindings |
| `examples/example-ir.md` | Example IR snippets |
| `examples/example-binding-manifest.md` | Example binding manifest |
| `operations/runbook.md` | Operational runbook and incident response |
| `operations/error-codes.md` | Per-code `SandboxErrorCode` reference and operator guidance |
| `checklists/security-review.md` | Security checklist |
| `checklists/release-readiness.md` | Release checklist |
| `references.md` | External references used |

## One-sentence architecture

```text
JSON IR -> import -> canonicalize -> type check -> effect check -> capability policy -> execution plan -> direct IR interpreter or compiled runtime artifact -> runtime facades -> audit
```

## Preferred default

Start with interpreted mode first. It is easier to make correct, easier to debug, and gives you the same validation/policy layer that compiled mode needs anyway. Add compiled mode after the IR, policy, and binding model are stable.
