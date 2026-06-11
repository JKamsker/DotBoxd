# ADR 0002 — Support Interpreted and Compiled Backends

## Status

Accepted.

## Context

Some sandboxed code is rarely executed and does not justify IL generation, DLL caching, verifier cost, or assembly loading. Other code may run frequently and needs better throughput.

## Decision

The sandbox will support two execution backends over the same verified execution plan:

1. interpreted mode, which walks the verified IR directly
2. compiled mode, which creates a compiler-owned runtime form and invokes its delegate

Interpreted mode is first-class and suitable for quick, rare, debug, or low-trust executions.
It must not emit IL, create a `DynamicMethod`, load a DLL, or interpret IL bytes. Compiled mode
is an optimization for hot/reused code. Any emitted IL is executed only by the CLR through a
created `DynamicMethod` delegate or a loaded verified managed assembly.

## Consequences

Positive:

- MVP can ship with interpreter only
- debugging is easier
- no IL, `DynamicMethod`, or DLL is required for rare runs
- compiled mode can be added later
- differential tests improve confidence

Negative:

- two backends need parity tests
- resource accounting must align
- host must choose backend or use auto mode

## Rule

Both modes must use the same IR validation, type checking, effect analysis, policy resolution, bindings, budgets, and audit model.
The interpreter depends on the IR/runtime layer, not on the compiler or generated-code verifier.
