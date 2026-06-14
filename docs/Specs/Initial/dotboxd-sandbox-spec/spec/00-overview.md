# 00 — Overview

## Summary

The system provides a safe execution environment for user-authored logic inside a C#/.NET host.

Users do not write C#. They submit restricted JSON IR documents. The IR can only express operations that the sandbox understands. Any operation with side effects, such as file access, network access, game-state mutation, randomness, clock access, or database access, must be represented as an explicit sandbox operation and must be granted by host policy.

The same verified IR can be executed in two ways:

```text
Interpreted mode:
    IR -> verified execution plan -> direct IR interpreter -> safe host APIs

Compiled mode:
    IR -> verified execution plan -> compiled runtime artifact -> verifier/gate -> delegate/load -> safe host APIs
```

Interpreted mode is for quick, rare, low-volume executions. Compiled mode is for hot or frequently reused logic.

## Why IR instead of C#?

C# is too expressive for an in-process sandbox. Even with limited references and API scanning, C# and the .NET runtime contain many escape hatches:

- reflection
- dynamic binding
- delegate construction
- expression tree compilation
- runtime code generation
- P/Invoke and native handles
- arbitrary object graphs
- service locators
- environment/process/thread APIs
- subtle APIs that indirectly expose IO or runtime metadata

A restricted IR avoids this by construction. The user cannot call arbitrary methods, cannot name arbitrary CLR types, cannot load assemblies, and cannot express raw IL.

## Core idea

The IR should describe intent, not .NET implementation details.

Bad model:

```text
User IR says: call System.IO.File.ReadAllText("foo.txt")
Sandbox tries to rewrite/block it later
```

Good model:

```text
User IR says: file.readText("foo.txt")
Policy checks: does this module have ReadFile?
Lowering emits/interprets: SafeFileSystem.ReadText(ctx, "foo.txt")
```

The IR does not know that `System.IO.File` exists.

## Security boundary

The in-process security boundary is semantic:

```text
User can only express allowed IR operations.
Compiler/interpreter only implement those operations.
Generated IL exists only in compiled mode. DotBoxd.Kernels never interprets IL; the current trusted
compiler emits a verified generated assembly/DLL, and the CLR executes that loaded runtime form.
`DynamicMethod` is a future backend only after an equivalent gate exists.
Host APIs are narrow capability facades.
```

For hard security boundaries, use an OS boundary:

```text
Main host process
    -> sandbox worker process/container/restricted account
        -> direct IR interpreter or compiled backend
```

This is especially important when the threat model includes malicious tenants, memory exhaustion, denial-of-service, runtime bugs, or defense in depth against unknown .NET escape paths.

## Runtime modes

### Interpreted mode

Use when:

- execution is rare
- logic is small
- startup latency matters more than throughput
- debugging/stepping is useful
- you do not want to generate or load assemblies
- you want the safest initial implementation

Properties:

- no IL, `DynamicMethod`, DLL, or interpreter bytecode emitted
- no `AssemblyLoadContext` needed
- same IR validator and policy system
- easy fuel checks
- easy tracing
- lower peak performance

### Compiled mode

Use when:

- the same IR runs often
- the code is on a hot path
- interpretation overhead matters
- startup compile cost can be amortized
- DLL caching is useful

Properties:

- emits a compiler-owned runtime artifact, not a raw MSIL blob supplied by a user
- current supported runtime form is a verified generated assembly/DLL
- `DynamicMethod` is future-only until equivalent pre-invocation gating exists
- post-emission verification/gating is mandatory before execution
- generated DLLs are cached by IR/policy/binding/runtime hash when using the assembly backend
- generated assemblies are loaded through a controlled `AssemblyLoadContext`
- faster steady-state execution, but more moving parts

## Trust model

There are three different actors:

| Actor | Can do |
|---|---|
| Script author | Writes JSON IR and can request capabilities |
| Host/server owner | Grants capabilities and registers bindings |
| Sandbox runtime | Enforces the verified plan |

A script author must never be able to grant their own .NET API bindings.

## Default behavior

Default behavior is deny-all except pure computation.

Allowed by default:

- primitive arithmetic
- comparisons
- booleans
- local variables
- bounded control flow
- pure math functions from the sandbox facade
- sandbox collections with budgets

Denied by default:

- filesystem
- network
- process/environment
- reflection/runtime metadata
- native interop
- threads/tasks
- arbitrary CLR object access
- direct database access
- direct game-state mutation

## Expected output of the project

The final system should make this true:

```text
Given any accepted IR module and policy P,
execution can only perform effects included in P,
can only call host bindings granted by P,
can only access host resources through safe facades,
and can be audited by module hash, policy hash, binding version, and effect log.
```
