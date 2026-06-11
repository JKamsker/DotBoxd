# 10 — Compiled Mode

## Purpose

Compiled mode turns verified IR into a compiler-owned runtime form and invokes an entrypoint
delegate created from that compiled form.
Supported forms are a `DynamicMethod` backend or a valid managed .NET assembly that can optionally
be persisted as a DLL cache artifact.

Compiled mode is for hot code paths and repeated execution.

## Important distinction

The system does not load raw MSIL and never treats interpreted mode as an IL execution path.
Compiled mode does not interpret IL either: emitted IL is executed only by the CLR after trusted code
creates a gated `DynamicMethod` delegate or loads a verified generated assembly and creates an
entrypoint delegate.

For an assembly backend, it emits a valid managed assembly image containing:

- PE/COFF structure
- CLR header
- metadata tables
- type definitions
- method definitions
- member references
- IL method bodies

Then it verifies and loads that assembly.

For a `DynamicMethod` backend, the compiler emits IL through trusted code only and creates a delegate
directly. This backend is acceptable only with equivalent allowlist gating for emitted calls, opcodes,
and runtime stubs. Users still never provide IL bytes, metadata tokens, or CLR member names.

## Compiler pipeline

```text
ExecutionPlan
  -> backend lowering
  -> generate DynamicMethod or assembly
  -> verify/gate generated runtime form
  -> create/load entrypoint delegate
  -> execute
```

The compiled runner accepts only an already-created runtime-form delegate plus proof that the
form was verified or gated. It does not accept raw IL as executable input.

## Assembly generation options

### Option A: `PersistedAssemblyBuilder`

Use for .NET versions that support saving Reflection.Emit-generated assemblies.

Pros:

- high-level Reflection.Emit API
- easier than writing metadata manually
- can save to stream/file

Cons:

- less low-level control than manual metadata writing
- still requires post-verification

### Option B: `System.Reflection.Metadata`

Use `MetadataBuilder`, `ManagedPEBuilder`, and related APIs to generate the assembly directly.

Pros:

- compiler-grade control
- deterministic output possible
- no Reflection.Emit dependency

Cons:

- much more complex
- easy to get metadata wrong

Recommendation:

Start with `PersistedAssemblyBuilder` if available and sufficient. Keep the compiler abstraction separate so the backend can later move to `System.Reflection.Metadata`.

### Option C: `DynamicMethod`

Use for an in-memory compiled delegate when no DLL artifact is needed.

Pros:

- no assembly file to persist or load
- lower startup overhead for one-process hot paths
- direct delegate creation

Cons:

- verifier support must be designed differently from DLL metadata verification
- no persistent DLL cache artifact
- easier to accidentally expand the emitted IL surface

Recommendation:

Use only after the assembly backend and verifier rules are proven, or keep the surface identical to
the assembly backend runtime stubs.

## Generated assembly shape

Keep generated assemblies boring.

Example shape:

```csharp
namespace Sandbox.Generated
{
    public static class Module_Abc123
    {
        public static SandboxValue Execute(SandboxContext ctx, SandboxValue input)
        {
            // generated code
        }
    }
}
```

Prefer:

- one public entrypoint type
- private helper methods only
- no custom attributes except approved debug metadata
- no embedded resources unless explicitly needed
- no static constructors
- no mutable static fields
- no finalizers
- no threads/tasks

## Generated code rules

Generated code may:

- use locals
- branch
- perform primitive arithmetic
- call approved runtime stubs
- call approved budget methods
- create approved sandbox values
- return sandbox values

Generated code must not:

- call arbitrary CLR methods
- reference arbitrary CLR types
- use reflection
- load assemblies
- use P/Invoke
- use function pointers/calli
- use `ldtoken` unless explicitly justified and verified
- create threads/tasks
- access environment/process APIs
- directly access files/network/database
- allocate arbitrary arrays/objects outside the safe model
- mutate static state

## Runtime stubs

Compiled code should call a very small set of runtime stubs.

Example:

```csharp
public static class CompiledRuntime
{
    public static void ChargeFuel(SandboxContext ctx, int amount);
    public static SandboxValue AddI32(SandboxValue a, SandboxValue b);
    public static SandboxValue CallBinding(SandboxContext ctx, int bindingSlot, SandboxValue[] args);
    public static SandboxValue NewString(string value);
    public static SandboxValue ListAdd(SandboxContext ctx, SandboxValue list, SandboxValue item);
}
```

Verifier allowlist can then be small:

```text
Sandbox.Runtime.CompiledRuntime.* exact allowed methods
Sandbox.Runtime.SandboxContext exact allowed methods
Sandbox.Runtime.SandboxValue exact allowed constructors/accessors
```

## Typed fast path

Later, generated code may use primitive CLR locals internally:

```csharp
int x = ...;
int y = x + 1;
```

This is acceptable if verifier rules remain strict.

Boundary conversion:

```text
SandboxValue -> typed local at function start
Typed local -> SandboxValue at function return / binding call
```

Do not add typed fast path until boxed-value mode is correct and tested.

## Fuel injection

Compiler must inject fuel charges at:

- function entry
- loop backedges
- before expensive operations
- before host calls
- collection growth
- string/bytes operations

Example pattern:

```text
loopStart:
    call CompiledRuntime.ChargeFuel(ctx, loopCost)
    ... body ...
    br loopStart
```

## Cancellation

Fuel charging should also observe cancellation:

```csharp
public static void ChargeFuel(SandboxContext ctx, int amount)
{
    ctx.CancellationToken.ThrowIfCancellationRequested();
    ctx.Budget.ChargeFuel(amount);
}
```

## DLL cache artifact

A compiled artifact consists of:

```text
module.dll
module.pdb optional
manifest.json
verification.json optional
```

The manifest contains:

```json
{
  "artifactVersion": 1,
  "moduleHash": "...",
  "planHash": "...",
  "policyHash": "...",
  "bindingManifestHash": "...",
  "runtimeFacadeHash": "...",
  "compilerVersion": "...",
  "verifierVersion": "...",
  "targetFramework": "net10.0",
  "assemblyHash": "...",
  "createdAt": "..."
}
```

## Loading

Use a controlled `AssemblyLoadContext` for version/loading isolation.

Important:

- this is not a security boundary
- load only after verification
- prefer collectible contexts when unloading is needed
- avoid loading multiple unbounded copies of generated code
- cache delegates carefully

## Delegate creation

After load:

```csharp
var type = assembly.GetType("Sandbox.Generated.Module_Abc123", throwOnError: true);
var method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
var del = method.CreateDelegate<SandboxCompiledEntrypoint>();
```

Do this once per loaded artifact, not per invocation.

## Static state

Generated code should not use mutable static state.

Allowed:

- readonly constants generated by compiler if verifier understands them

Forbidden:

- static constructors
- static mutable fields
- thread-static fields
- async locals
- caches inside generated code

Host-managed caches belong outside generated assemblies.

## Async

MVP recommendation: compiled entrypoints are synchronous and host calls are synchronous wrappers only if safe.

If async host calls are required, either:

- keep those modules interpreted, or
- compile to an async state machine only after verifier supports generated async patterns, or
- use runtime stubs returning `ValueTask<SandboxValue>` with carefully verified calls

Async generated code greatly expands verifier complexity.

Recommendation:

Start with synchronous IR. Model async at the host scheduling layer.

## Fallback

If compilation fails, host may interpret if policy allows.

If verification fails, do not run compiled code. Quarantine the artifact and optionally interpret the original verified plan.

## Performance expectations

Compiled mode saves interpreter overhead and can amortize IR processing and compile time through caching.

It does not necessarily remove JIT cost for normal IL assemblies. First execution of generated methods may still JIT. Persistent DLL cache saves IR-to-DLL compilation, not necessarily native-code compilation.

## Compiler acceptance criteria

Compiled mode is acceptable only when:

- all generated assemblies pass verifier
- interpreter/compiler differential tests pass
- cache invalidation is correct
- all host calls route through approved stubs
- no forbidden metadata/opcodes are emitted
- fuel checks are present on all loops/calls
- policy hash is embedded/associated with the artifact
- audit indicates compiled mode and artifact hash
