# Tutorial: your first Kernel (sandbox)

This tutorial walks you through running a **kernel** end to end: importing restricted JSON IR,
validating it against a hard resource policy, and executing it inside a fuel-metered sandbox. By the
end you will have a small program that computes a value from untrusted logic and reports exactly how
much fuel that logic burned — with a guarantee that a buggy or hostile kernel cannot run away with
host resources.

Every code and JSON snippet below is taken from, or verified against, the real DotBoxD source. Inline
citations point at the exact files.

## What a kernel is (and is not)

A **kernel** is a unit of logic a client hands to the host as **restricted JSON IR** — never C#, never
IL, never CLR member names, assemblies, reflection, or arbitrary host calls. The host imports that IR,
validates it against a capability/resource policy, and only then executes it inside a metered sandbox.

> A kernel is restricted JSON IR (never C#, IL, or arbitrary host calls). The host imports it,
> validates it against a capability/resource policy, and executes it inside a fuel-metered sandbox.
> — `README.md`, section "2. Kernels"

This is the library's real trust boundary. From `README.md` ("Security: what is and isn't a boundary"):

- **Safe mode is the real boundary.** A kernel is validated, capability-gated, fuel/quota-metered,
  and (for compiled mode) verified before it runs. Users never supply C#, raw IL, CLR member names,
  assemblies, or arbitrary host calls.
- Trusted-plugin mode (normal .NET assemblies via `AssemblyLoadContext`) is **not** a security
  boundary. Hard multi-tenant isolation of untrusted arbitrary .NET requires an out-of-process / OS
  boundary.

The IR the sandbox actually accepts is a small, closed language: a module with functions, whose bodies
are statements (`set`, `return`, `if`, `while`) over expressions (`var`, arithmetic ops like `add` /
`mul`, and typed literals like `{ "i32": 10 }`). Anything outside that closed set — for example a
`System.IO.File.ReadAllText` call or a CLR-shaped type name — is rejected at import/prepare time, not
at run time (see [Safety guarantees](#safety-guarantees-at-a-glance) below).

## What you will build

A host that runs a "loot score" kernel. The kernel takes two `I32` inputs — `level` and `rarity` —
and returns `level * 10 + rarity * 25`. This is the real `loot-score` kernel used by the test suite
(`tests/DotBoxD.Kernels.Tests/_TestSupport/SandboxTestHost.cs`), and the interpreter test asserts that
inputs `(3, 2)` produce `80`
(`tests/DotBoxD.Kernels.Tests/Interpreter/InterpreterAndPolicyTests.cs`).

## Prerequisites

The kernel stack targets `net10.0`. Install the pieces you need, or the `DotBoxD` meta-package for the
whole stack (`README.md`, "Installing from NuGet"):

```bash
# Everything (Services + Kernels + Pushdown):
dotnet add package DotBoxD --prerelease

# ...or just the kernel host, an execution backend, safe bindings, and the JSON importer:
dotnet add package DotBoxD.Hosting --prerelease
dotnet add package DotBoxD.Kernels.Interpreter --prerelease
dotnet add package DotBoxD.Kernels.Runtime --prerelease
dotnet add package DotBoxD.Kernels.Serialization.Json --prerelease
```

- `DotBoxD.Hosting` gives you `SandboxHost` (import, prepare, execute).
- `DotBoxD.Kernels.Interpreter` is the direct IR execution backend (`UseInterpreter`).
- `DotBoxD.Kernels.Runtime` supplies the safe host bindings, including `AddDefaultPureBindings`.
- `DotBoxD.Kernels.Serialization.Json` provides the `ImportJsonAsync` / `JsonExporter` round-trip.

## Step 1 — Create a sandbox host

`SandboxHost.Create` takes a builder callback. For a purely computational kernel you only need the
default **pure** bindings (math, strings — nothing that touches the filesystem, clock, or network) and
an execution backend. `UseInterpreter` selects the interpreter backend.

```csharp
using DotBoxD.Hosting.Execution;   // SandboxHost, SandboxHostBuilder
using DotBoxD.Kernels;             // ExecutionPlan, ExecutionMode, SandboxExecutionOptions
using DotBoxD.Kernels.Policies;    // SandboxPolicyBuilder
using DotBoxD.Kernels.Sandbox;     // SandboxValue, SandboxType, I32Value, SandboxErrorCode

// A sandbox host with only the safe, pure bindings enabled.
var host = SandboxHost.Create(builder =>
{
    builder.AddDefaultPureBindings();
    builder.UseInterpreter();
});
```

`SandboxHost.Create` and the `AddDefaultPureBindings` / `UseInterpreter` builder methods are the exact
shapes used in `README.md` (section 2) and in
`src/Hosting/DotBoxD.Hosting/Execution/Host/SandboxHostBuilder.cs`. `SandboxHost` lives in the
`DotBoxD.Hosting.Execution` namespace (see `samples/GameServer/Examples.GameServer.Server/Simulation/GameWorldHost.cs`,
which opens `using DotBoxD.Hosting.Execution;`).

`SandboxHost` is `IDisposable`, so in a real app wrap it in `using` or dispose it when you are done.

## Step 2 — Build a policy (the hard budget)

A `SandboxPolicy` is a **hard budget**: fuel, loop iterations, list length, and capability grants. The
builder in `src/Kernels/DotBoxD.Kernels/Policies/SandboxPolicyBuilder.cs` exposes fluent `With*`
methods; each returns a new limit on the underlying `ResourceLimits`.

```csharp
// A policy is a hard budget: fuel, loop iterations, list length, capability grants.
var policy = SandboxPolicyBuilder.Create()
    .WithFuel(1_000_000)
    .WithMaxLoopIterations(10_000)
    .WithMaxListLength(10_000)
    .Build();
```

- `WithFuel(long)` caps total execution "fuel"; when it runs out, execution stops with a quota error.
- `WithMaxLoopIterations(long)` bounds `while`/loop iteration counts.
- `WithMaxListLength(int)` bounds the size of any list value the kernel builds or receives.

This is the verbatim policy shape from `README.md` section 2. The builder also offers grants such as
`GrantFileRead`, `GrantTimeNow`, and `GrantRandom` for kernels that need side effects — our pure loot
kernel needs none, so we grant nothing.

## Step 3 — Provide the kernel JSON IR

Below is a real, complete kernel module — the `loot-score` kernel from
`tests/DotBoxD.Kernels.Tests/_TestSupport/SandboxTestHost.cs`. It declares one entrypoint function
`main` with two `I32` parameters and a body of three statements.

```json
{
  "id": "loot-score",
  "version": "1.0.0",
  "targetSandboxVersion": "1.0.0",
  "capabilityRequests": [],
  "functions": [
    {
      "id": "main",
      "visibility": "entrypoint",
      "parameters": [
        { "name": "level", "type": "I32" },
        { "name": "rarity", "type": "I32" }
      ],
      "returnType": "I32",
      "body": [
        {
          "op": "set",
          "name": "base",
          "value": { "op": "mul", "left": { "var": "level" }, "right": { "i32": 10 } }
        },
        {
          "op": "set",
          "name": "bonus",
          "value": { "op": "mul", "left": { "var": "rarity" }, "right": { "i32": 25 } }
        },
        {
          "op": "return",
          "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "bonus" } }
        }
      ]
    }
  ]
}
```

Reading the shape:

- The **module** has an `id`, a `version`, an optional `targetSandboxVersion`, a `capabilityRequests`
  list (empty here — a pure kernel needs no capabilities), and a `functions` array.
- A **function** with `"visibility": "entrypoint"` can be invoked by name (here `"main"`). Its
  `parameters` are typed and positional; `returnType` is the value type it yields.
- The **body** is a list of statements. `set` binds a local (`base`, `bonus`); `return` yields an
  expression. Expressions are either a literal (`{ "i32": 10 }`), a variable read (`{ "var": "level" }`),
  or a binary op (`{ "op": "mul", "left": ..., "right": ... }`).

The `op`/`left`/`right`/`var`/`i32` grammar here is exactly what the importer accepts; anything else
(for example `{ "op": "pow", ... }` or a CLR type name) is rejected — see
`tests/DotBoxD.Kernels.Tests/Serialization/JsonImporterTests.cs`.

> **How does a client obtain such JSON?** You rarely hand-write it. The typical producer builds a
> `SandboxModule` (or lowers a C# method to IR via the analyzer) and serializes it with
> `JsonExporter.Export(module, indented: true)` from `DotBoxD.Kernels.Serialization.Json`
> (`src/Kernels/DotBoxD.Kernels.Serialization.Json/JsonExporter.cs`), which returns the JSON string
> shown above. Import and export are a round trip: `JsonImporter` reads it back into a `SandboxModule`.

In C#, keep the JSON in a string (or read it from a file):

```csharp
const string kernelJson = /* the JSON module above */;
```

## Step 4 — Import, prepare, execute

Three calls take you from JSON to a result:

1. `ImportJsonAsync` parses the JSON into a `SandboxModule`. This method is an extension in
   `DotBoxD.Kernels.Serialization.Json.Hosting`
   (`src/Kernels/DotBoxD.Kernels.Serialization.Json/Hosting/SandboxHostJsonExtensions.cs`).
2. `PrepareAsync` **validates** the module against the policy and bindings, then builds a sealed
   `ExecutionPlan`. If validation fails it throws `SandboxValidationException`
   (`src/Hosting/DotBoxD.Hosting/Execution/Host/SandboxHost.cs`).
3. `ExecuteAsync` runs a named entrypoint with an input value and returns a `SandboxExecutionResult`.

The entrypoint's parameters are supplied **positionally** as the elements of the input list: the loot
kernel's `[level, rarity]` come from a two-element list `[3, 2]`, so `level = 3` and `rarity = 2`.
This is the exact call the interpreter test makes
(`tests/DotBoxD.Kernels.Tests/Interpreter/InterpreterAndPolicyTests.cs`).

```csharp
using DotBoxD.Kernels.Serialization.Json.Hosting; // ImportJsonAsync

var module = await host.ImportJsonAsync(kernelJson);
var plan = await host.PrepareAsync(module, policy);

// The entrypoint takes two I32 parameters; supply them positionally as a list.
var input = SandboxValue.FromList(
[
    SandboxValue.FromInt32(3),  // level
    SandboxValue.FromInt32(2),  // rarity
]);

var result = await host.ExecuteAsync(
    plan,
    "main",
    input,
    new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
```

`SandboxValue.FromInt32` and `SandboxValue.FromList` are the real factories from
`src/Kernels/DotBoxD.Kernels/Sandbox/SandboxValue.cs`. When you need an explicit item type — for
example a list whose element type must be pinned even when empty — use the two-argument overload from
`README.md` section 2:

```csharp
// Same factory, with an explicit element type (README section 2).
var typedInput = SandboxValue.FromList(
    [.. new[] { 3, 2 }.Select(SandboxValue.FromInt32)],
    SandboxType.I32);
```

`SandboxType.I32` is a built-in scalar type from `src/Kernels/DotBoxD.Kernels/Sandbox/SandboxType.cs`.

> Passing execution options is optional: `host.ExecuteAsync(plan, "main", input)` also compiles and
> uses the host's default options (`README.md` section 2). We pass `ExecutionMode.Interpreted`
> explicitly here to match the host we built.

## Step 5 — Read the result

`SandboxExecutionResult` (`src/Kernels/DotBoxD.Kernels/ExecutionPlan.cs`) carries success, the return
value, any error, and a full `ResourceUsage` snapshot. Pattern-match the value to its concrete type:

```csharp
if (result.Succeeded && result.Value is I32Value total)
{
    // A buggy or hostile kernel cannot run away with host resources:
    Console.WriteLine($"total={total.Value}, fuel burned={result.ResourceUsage.FuelUsed}");
    // For input (level: 3, rarity: 2): total = 3*10 + 2*25 = 80.
}
else
{
    Console.WriteLine($"failed: {result.Error?.Code} — {result.Error?.SafeMessage}");
}
```

- `result.Succeeded` — did the run complete without a policy/quota/validation failure?
- `result.Value` — the returned `SandboxValue`; here it is an `I32Value`, whose `.Value` is the `int`
  (`src/Kernels/DotBoxD.Kernels/Sandbox/SandboxValue.cs`).
- `result.ResourceUsage.FuelUsed` — the fuel this run actually consumed
  (`SandboxResourceUsage`, `src/Kernels/DotBoxD.Kernels/Sandbox/SandboxResourceUsage.cs`). The same
  record also exposes `LoopIterations`, `HostCalls`, `AllocatedBytes`, `CollectionElements`, and more.
- On failure, `result.Error` is a `SandboxError` with a `Code` (`SandboxErrorCode`) and a redaction-safe
  `SafeMessage` (`src/Kernels/DotBoxD.Kernels/Sandbox/SandboxError.cs`).

For the loot kernel with `(3, 2)`, `total.Value` is `80` — exactly what the interpreter test asserts.

## Safety guarantees at a glance

The three defenses layer up, and the first two happen **before your logic ever runs**:

### 1. Validation (import + prepare)

`PrepareAsync` runs the module validator and refuses to build a plan for anything malformed or
disallowed, throwing `SandboxValidationException`. This is where CLR escape hatches die: a kernel that
tries to call `System.IO.File.ReadAllText` is rejected with diagnostic `E-IR-CLR-REF` *before*
execution (`tests/DotBoxD.Kernels.Tests/Serialization/JsonImporterTests.cs`,
`Forbidden_clr_call_is_rejected_before_execution`). Unknown operators (`E-JSON-OP`), wrong literal
kinds (`E-JSON-TYPE`), and duplicate parameters (`E-STRUCT-DUP-PARAM`) are likewise caught here.

### 2. Capabilities (least privilege)

Bindings with side effects are gated by explicit policy grants. Our host added only
`AddDefaultPureBindings`, and our policy granted nothing, so a kernel that tried to read a file would
fail preparation with `E-POLICY-CAP` — the host never granted `file.read`
(`tests/DotBoxD.Kernels.Tests/Interpreter/InterpreterAndPolicyTests.cs`,
`File_read_is_denied_without_host_grant`). To allow a side effect you must opt in deliberately, e.g.
`SandboxPolicyBuilder.Create().GrantFileRead(root, maxBytesPerRun)`.

### 3. Fuel and quotas (bounded execution)

Even valid, permitted logic runs under a hard budget. An infinite loop does not hang the host: fuel
runs out and the run ends with `result.Succeeded == false` and
`result.Error.Code == SandboxErrorCode.QuotaExceeded`
(`tests/DotBoxD.Kernels.Tests/Interpreter/InterpreterAndPolicyTests.cs`,
`Fuel_exhaustion_stops_infinite_loop`). `WithMaxLoopIterations`, `WithMaxListLength`,
`WithMaxHostCalls`, `WithWallTime`, and the other `With*` limits give you independent hard ceilings on
each dimension of resource use.

Because those three layers are enforced by the host — not by the kernel author — you can accept
kernels from clients you do not fully trust and still bound their blast radius. (For the strong
multi-tenant isolation story and its limits, read the caveats page linked below.)

## Full example

```csharp
using DotBoxD.Hosting.Execution;                   // SandboxHost
using DotBoxD.Kernels;                             // ExecutionMode, SandboxExecutionOptions
using DotBoxD.Kernels.Policies;                    // SandboxPolicyBuilder
using DotBoxD.Kernels.Sandbox;                     // SandboxValue, I32Value
using DotBoxD.Kernels.Serialization.Json.Hosting;  // ImportJsonAsync

const string kernelJson = """
{
  "id": "loot-score",
  "version": "1.0.0",
  "targetSandboxVersion": "1.0.0",
  "capabilityRequests": [],
  "functions": [
    {
      "id": "main",
      "visibility": "entrypoint",
      "parameters": [
        { "name": "level", "type": "I32" },
        { "name": "rarity", "type": "I32" }
      ],
      "returnType": "I32",
      "body": [
        { "op": "set", "name": "base",  "value": { "op": "mul", "left": { "var": "level" },  "right": { "i32": 10 } } },
        { "op": "set", "name": "bonus", "value": { "op": "mul", "left": { "var": "rarity" }, "right": { "i32": 25 } } },
        { "op": "return", "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "bonus" } } }
      ]
    }
  ]
}
""";

using var host = SandboxHost.Create(builder =>
{
    builder.AddDefaultPureBindings();
    builder.UseInterpreter();
});

var policy = SandboxPolicyBuilder.Create()
    .WithFuel(1_000_000)
    .WithMaxLoopIterations(10_000)
    .WithMaxListLength(10_000)
    .Build();

var module = await host.ImportJsonAsync(kernelJson);
var plan = await host.PrepareAsync(module, policy);

var input = SandboxValue.FromList(
[
    SandboxValue.FromInt32(3),  // level
    SandboxValue.FromInt32(2),  // rarity
]);

var result = await host.ExecuteAsync(
    plan,
    "main",
    input,
    new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

if (result.Succeeded && result.Value is I32Value total)
{
    Console.WriteLine($"total={total.Value}, fuel burned={result.ResourceUsage.FuelUsed}");
}
else
{
    Console.WriteLine($"failed: {result.Error?.Code} — {result.Error?.SafeMessage}");
}
```

Expected output (the value is deterministic; the exact fuel figure depends on the runtime version):

```text
total=80, fuel burned=<n>
```

## Next steps

- [Kernels — concepts](../concepts/kernels.md) — the IR model, value types, and execution modes in
  depth.
- [Sandbox caveats & the trust boundary](../security/sandbox-caveats.md) — what safe mode does and
  does not protect against, and when you need out-of-process isolation.
- [Pushing logic server-side (server extensions)](./pushdown-server-extension.md) — let a plugin ship
  its own sandboxed batch operation so N round-trips collapse into one.
