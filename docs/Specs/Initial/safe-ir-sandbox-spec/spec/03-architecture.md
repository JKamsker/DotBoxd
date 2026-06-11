# 03 — Architecture

## Component diagram

```text
+-------------------+
| User JSON IR      |
+---------+---------+
          |
          v
+-------------------+       +-------------------+
| JSON IR Importer  | ----> | Diagnostics       |
+---------+---------+       +-------------------+
          |
          v
+-------------------+
| Canonical IR      |
+---------+---------+
          |
          v
+-------------------+       +-------------------+
| Type Checker      | ----> | Type Diagnostics  |
+---------+---------+       +-------------------+
          |
          v
+-------------------+       +-------------------+
| Effect Analyzer   | ----> | Required Effects  |
+---------+---------+       +-------------------+
          |
          v
+-------------------+       +-------------------+
| Policy Resolver   | ----> | Capability Grants |
+---------+---------+       +-------------------+
          |
          v
+-------------------+
| Execution Plan    |
+----+----------+---+
     |          |
     |          |
     v          v
+------------+ +-----------------+
| Direct IR  | | Compiler        |
| Interpreter| +--------+--------+
+-----+------+          |
     |                v
     |       +-----------------+
     |       | DynamicMethod or|
     |       | Generated DLL   |
     |       +--------+--------+
     |                |
     |                v
     |       +-----------------+
     |       | Verifier/Gate   |
     |       +--------+--------+
     |                |
     +--------+-------+
              v
+-------------------+
| Sandbox Runtime   |
| Safe Facades      |
+---------+---------+
          |
          v
+-------------------+
| Host Resources    |
+-------------------+
```

## Main packages

### `Sandbox.Core`

Contains:

- IR model
- type model
- effect model
- diagnostics
- canonicalizer
- serializer
- execution-plan model

Must not depend on Reflection.Emit or host app infrastructure.

### `Sandbox.Validation`

Contains:

- structural validation
- type checking
- effect inference
- policy validation
- binding signature validation
- resource-cost analysis

### `Sandbox.Runtime`

Contains:

- `SandboxContext`
- safe value representation
- safe collections
- safe host facades
- fuel and quota accounting
- audit sink abstractions
- binding invocation abstractions

### `Sandbox.Interpreter`

Contains:

- direct IR interpreter over the validated `SandboxModule`
- debug stepping
- trace events
- interpreter-specific optimizations

### `Sandbox.Compiler`

Contains:

- IR lowering to a compiled runtime form
- `DynamicMethod` generation or managed assembly generation
- compiled delegate creation
- cache artifact writer
- generated symbol/debug info where needed

### `Sandbox.Verifier`

Contains:

- generated assembly metadata verifier
- opcode verifier
- member reference verifier
- manifest verifier
- cache artifact verifier

### `Sandbox.Hosting`

Contains:

- high-level public API
- default policy builder
- binding registry builder
- execution-mode selector
- worker-process client if used

## End-to-end pipeline

### 1. Import JSON IR

Input may be:

- a JSON IR document
- prebuilt IR from a visual editor
- host-generated IR

Output is a raw module representation.

### 2. Canonicalize

Canonicalization normalizes semantically equivalent IR:

- stable function ordering
- stable local IDs
- resolved symbol names
- normalized constants
- removed dead declarations where safe
- deterministic serialization

Canonical form is used for hashing and cache keys.

### 3. Validate structure

Reject:

- duplicate symbols
- invalid references
- invalid control-flow graphs
- unreachable mandatory blocks
- malformed constants
- invalid type annotations
- unsupported IR version
- unknown operation IDs

### 4. Type check

Resolve all expression, instruction, function, and host-call types.

Reject:

- implicit object/dynamic behavior
- invalid generic instantiations
- invalid conversions
- nullable/option misuse
- collection element mismatch
- host binding signature mismatch

### 5. Infer effects

Each function gets an effect set. Effects are the union of:

- intrinsic operation effects
- called function effects
- host binding effects
- allocation/cpu effects

Example:

```json
{
  "id": "loadConfig",
  "returnType": "String",
  "body": [
    {
      "op": "return",
      "value": { "call": "file.readText", "args": [{ "path": "config.json" }] }
    }
  ]
}
```

```text
Effects(loadConfig) = Cpu | Alloc | FileRead
```

### 6. Resolve policy

Compare required effects/capabilities with granted policy.

Reject if any required capability is missing.

If a capability has parameters, bind them here:

```text
file.read:
    roots = ["/srv/tenant/123/data"]
    maxBytesPerRun = 1_000_000
```

### 7. Build execution plan

The execution plan is immutable and hashable.

It contains:

- canonical IR hash
- module metadata
- function table
- type table
- binding table
- granted capabilities
- resource budgets
- execution options
- function analysis

### 8. Select backend

Execution mode can be chosen by host options:

```csharp
ExecutionMode.Interpreted
ExecutionMode.Compiled
ExecutionMode.Auto
```

`Auto` should choose interpreted mode initially and compiled mode after hotness/cost thresholds.

### 9A. Interpret

The interpreter executes the verified IR held by the plan directly. It must not emit IL,
build a `DynamicMethod`, load a DLL, or run an interpreter bytecode layer.

It must:

- check fuel
- enforce quotas
- route host calls through binding descriptors
- emit audit events
- produce deterministic diagnostics

### 9B. Compile

The compiler emits a compiled runtime form: either a gated `DynamicMethod` delegate or a
verified .NET assembly/DLL loaded through controlled runtime code.

It must:

- emit only allowed method refs
- call safe runtime stubs/facades
- inject budget checks
- avoid arbitrary allocations
- save DLL and manifest when caching an assembly backend

The host invokes only the entrypoint delegate created from that runtime form. It must never
feed generated IL bytes into an interpreter.

### 10B. Verify generated assembly

The verifier checks:

- assembly refs
- type refs
- member refs
- method bodies
- opcodes
- P/Invoke/native metadata
- custom attributes
- public surface
- embedded resources
- manifest consistency

Only verified assemblies can be loaded/executed.

### 11. Execute

Execution receives:

- `SandboxContext`
- entrypoint name
- sandbox values/input
- cancellation token
- audit sink

Result is:

- value or error
- resource usage
- effects used
- audit log reference

## Recommended execution-mode selection

```text
Use interpreter when:
    runs <= 10
    estimated operations <= 10_000
    debugging enabled
    compile backend unavailable
    policy says no dynamic codegen

Use compiled mode when:
    same module is reused often
    estimated operations high
    execution is on hot path
    cache hit exists and verifier passes
```

The exact thresholds should be configurable.

## Failure model

All pipeline failures must be fail-closed.

Examples:

| Failure | Result |
|---|---|
| Unknown binding | Reject module |
| Unknown effect | Reject module |
| Verifier unavailable | Do not execute compiled artifact; optionally interpret |
| Cache manifest mismatch | Delete/quarantine cache entry; recompile or interpret |
| Policy conflict | Reject execution |
| Fuel exhausted | Stop execution with sandbox error |
| Host binding throws | Convert to sandbox error unless explicitly configured |

## Internal representation recommendation

Use the canonical typed IR as the backend-independent representation. Do not introduce a
separate interpreter bytecode layer.

Reason:

- easier validation
- easier interpretation
- easier effect inference
- easier compiler backend
- easier differential testing
- no need to expose .NET metadata concepts to users

Suggested layers:

```text
JSON IR        user-facing format
Canonical IR   stable, typed, named operations interpreted directly
IL artifact    compiled backend only, never user-facing or interpreted (`DynamicMethod` or DLL)
```
