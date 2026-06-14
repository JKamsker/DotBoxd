# 01 — Goals and Non-Goals

## Goals

### G1. Safe-by-construction user logic

Users submit restricted JSON IR that cannot express arbitrary .NET behavior.

The user must not be able to:

- call arbitrary CLR methods
- name arbitrary CLR types
- load assemblies
- use reflection
- use native interop
- create delegates to host methods
- access process/global state
- bypass sandbox APIs

### G2. Explicit capabilities

Every side-effecting operation must have an explicit capability.

Examples:

| Operation | Required capability |
|---|---|
| Read file | `file.read` |
| Write file | `file.write` |
| HTTP GET | `net.http.get` |
| Current time | `time.now` |
| Random number | `random` |
| Read game character | `game.character.read` |
| Mutate inventory | `game.inventory.write` |

The script can request capabilities, but only the host can grant them.

### G3. Same semantics in interpreter and compiler

Interpreted mode and compiled mode must execute the same verified IR semantics.

A module must not behave differently because it was interpreted instead of compiled, except for performance, stack traces, debug behavior, and timing-sensitive host calls.

### G4. Fast path without abandoning safety

Compiled mode should allow hot IR to become fast generated code while preserving:

- type checking
- effect checking
- capability enforcement
- resource accounting
- host facade routing
- post-emit verification
- cache invalidation

### G5. Persistence/cache

Compiled modules can be persisted as DLLs for faster future startup.

The cache must be invalidated when any relevant input changes:

- IR hash
- compiler version
- runtime facade version
- binding manifest hash
- policy hash
- target framework/runtime assumptions
- verifier version

### G6. Auditable execution

The system must be able to answer:

- who ran what
- which IR hash ran
- which policy was used
- which capabilities were granted
- which bindings were called
- which resources were touched
- whether it was interpreted or compiled
- which cached artifact was used

### G7. Practical host integration

The host should expose a small C# API:

```csharp
var module = await sandbox.ImportJsonAsync(jsonIr);
var plan = sandbox.Prepare(module, policy);
var result = await sandbox.ExecuteAsync(plan, input, options, cancellationToken);
```

The host should not need to reason about MSIL, metadata tokens, or verifier internals for normal usage.

## Non-goals

### NG1. Running arbitrary C# safely in-process

This is explicitly out of scope.

Do not support:

- arbitrary C# plugin source
- a custom text DSL parser
- Roslyn compile of user C# with “safe references”
- arbitrary assemblies uploaded by users
- raw MSIL upload
- “scan bad APIs and hope”

### NG2. Using `AssemblyLoadContext` as a sandbox

`AssemblyLoadContext` is useful for dependency/version isolation and unloading generated assemblies. It is not a security boundary.

### NG3. Code Access Security / partial trust

Modern .NET does not provide the old .NET Framework CAS sandbox model for this scenario. The sandbox must not depend on CAS or partial trust.

### NG4. Perfect in-process memory isolation

In-process memory limits are cooperative and best-effort. Hard memory limits require a separate process/container/job object/cgroup/restricted runtime environment.

### NG5. Protecting against runtime/JIT/OS vulnerabilities

The system reduces the attack surface, but it cannot prove the CLR, JIT, OS, CPU, or native libraries are bug-free.

For high-risk untrusted code, add process/container isolation.

### NG6. Full programming language compatibility

The IR should be intentionally smaller than C#.

Features like reflection, unsafe code, arbitrary generics, overloaded operator resolution, custom finalizers, destructors, dynamic dispatch, exceptions with arbitrary types, threads, tasks, and arbitrary object identity are not part of the initial language.

### NG7. Hidden developer/support bypasses

There must be no implicit bypass path. Any elevated behavior must be a named capability with audit logging.

## Design priorities

In order:

1. Security correctness
2. Predictability
3. Debuggability
4. Operational visibility
5. Interpreter/compiler semantic parity
6. Performance
7. Expressiveness

When in doubt, choose the smaller language and add features later.
