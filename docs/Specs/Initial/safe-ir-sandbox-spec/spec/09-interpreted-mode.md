# 09 — Interpreted Mode

## Purpose

Interpreted mode executes verified IR directly without compiling to IL, creating a `DynamicMethod`,
or loading a DLL. The interpreter is an IR interpreter, not an IL interpreter.

This is required for:

- quick one-off executions
- rare plugin hooks
- debugging
- fast iteration
- environments where runtime code generation is unavailable or undesirable
- early MVP before compiled backend is ready

## Core requirement

Interpreted mode and compiled mode must share:

- canonical IR
- type checker
- effect analyzer
- policy resolver
- binding registry
- resource budgets
- audit sink
- error model
- deterministic mode

Only the backend changes.

## Interpreter pipeline

```text
JSON IR
  -> import
  -> canonicalize
  -> validate
  -> type check
  -> effect check
  -> policy resolve
  -> execute verified IR in interpreter
```

No assembly, IL, dynamic method, DLL, or interpreter bytecode is emitted for interpreted mode.
The interpreter executes the verified `SandboxModule`/`ExecutionPlan` IR only.

## Execution representation

Interpreted mode walks the imported and validated IR object model directly. It executes
functions, statements, expressions, and calls from the same `SandboxModule` held by the
execution plan.

Pros:

- good diagnostics
- simple stepping
- no generated code or lowered instruction stream
- closest representation to user-submitted JSON IR

Cons:

- slower
- recursion depth concerns
- harder to optimize

Do not add an interpreter bytecode layer. If a lower-level representation is needed, it belongs
to compiled mode and must produce a compiler-owned runtime form such as a gated `DynamicMethod`
or a verified managed DLL before execution.

## Interpreter state

```csharp
public sealed class InterpreterFrame
{
    public FunctionId Function { get; }
    public Dictionary<string, SandboxValue> Locals { get; }
}

public sealed class InterpreterState
{
    public SandboxContext Context { get; }
    public Stack<InterpreterFrame> Frames { get; }
    public SandboxValue? ReturnValue { get; set; }
}
```

## Fuel accounting

Interpreter fuel checks are straightforward.

Charge fuel for:

- every statement/expression or grouped IR block
- loop backedges
- function calls
- host binding calls
- collection operations
- string/bytes operations

Example:

```csharp
context.ChargeFuel(operation.Cost);
```

Loop backedges should charge extra fuel.

## Binding calls

Interpreter calls binding descriptors directly:

```csharp
var binding = plan.Bindings.Get(bindingSlot);
context.RequireCapability(binding.RequiredCapability);
context.ChargeFuel(binding.CostModel.BaseFuel);
var result = await binding.Invoke(context, args, ct);
```

The interpreter must not use reflection to invoke arbitrary methods from user-controlled names.

## Debugging support

Interpreted mode should support:

- step over/into/out
- breakpoints by JSON location or IR node ID
- variable inspection
- trace host calls
- trace fuel usage
- deterministic replay when inputs/policy are stable

Debug traces should include:

```text
runId
moduleHash
functionId
statement/expression kind
jsonLocation optional
locals snapshot optional/limited
fuelRemaining
```

## Diagnostics

Interpreter errors should point to IR node IDs or JSON locations where available.

Examples:

```text
E-POLICY-001: capability file.read required by file.readText but not granted.
E-RUNTIME-004: fuel exhausted in function calculateLoot at loop starting line 42.
E-BINDING-007: file.readText denied path outside sandbox root.
```

## When to use interpreted mode

Use interpreted mode when:

```text
estimatedCost < small threshold
runCount < hotness threshold
debugging enabled
cache miss and compile latency not worth it
module uses features not supported by compiler yet
policy forbids dynamic code generation
host runs in constrained environment
```

## Auto mode

Auto mode should begin interpreted and promote to compiled after a threshold.
Until hotness tracking and a selector are implemented, `Auto` should behave like interpreted mode.

Suggested heuristic:

```text
if DebugEnabled: Interpreted
else if CompiledCacheHitAndVerified: Compiled
else if EstimatedOps < 10_000 and HistoricalRuns < 20: Interpreted
else CompileAndCacheThenRun
```

Thresholds must be configurable.

## Hotness tracking

Track by canonical execution-plan hash:

```text
planHash
runCount
averageDurationInterpreted
averageFuelUsed
lastRunAt
compileFailures
compiledArtifactHash optional
```

If interpretation becomes expensive, compile.

## Interpreter optimizations

Safe optimizations:

- constant folding during canonicalization
- direct binding slot lookup
- precomputed local indices
- small-value structs for primitives
- string interning only if controlled

Avoid optimizations that change semantics compared to compiled mode.

## Interpreter/compiler parity

Every accepted module should pass differential tests:

```text
interpret(plan, input) == compileAndRun(plan, input)
```

For nondeterministic effects, compare under deterministic injected clock/random/network fixtures.

## Failure behavior

If compiled mode fails because of:

- compiler unavailable
- verifier unavailable
- cache invalid
- unsupported backend feature

The host may fall back to interpreted mode only if:

- the same verified execution plan is used
- policy allows interpreted fallback
- audit records fallback reason

Never fall back to a less restrictive validation path.

## Benefits for sandboxing

Interpreted mode is often safer initially because:

- there is no generated IL to verify
- there is no assembly loading
- every operation is under direct runtime control
- fuel checks are trivial
- host calls are centralized

For a first production version, interpreted mode can be the default and compiled mode can be an optimization.
