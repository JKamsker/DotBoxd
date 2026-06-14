# Followups: `[KernelMethod]` inlining and kernel RPC services

Two follow-on features layered on the lowering pipeline and the kernel runtime. Both keep the SafeIR
invariant: plugin authors write plain C#, the generator lowers it to **verified, sandboxed IR**, and the
server never compiles plugin source.

---

## 1. `[KernelMethod]` — call reusable kernel methods from hooks

### Goal

Let a plugin author factor shared gate/handler logic out of a `Where`/`Select`/`InvokeKernel` lambda (or
a kernel-class `ShouldHandle`/`Handle`) into a named static helper, without leaving the sandbox:

```csharp
server.Hooks.On<MonsterAggroEvent>()
    .Where((e, ctx) => IsBullyingLowLevelPlayer(e.MonsterLevel, e.PlayerLevel, e.Distance, 3, 5, 5))
    .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));

[KernelMethod]
public static bool IsBullyingLowLevelPlayer(
    int monsterLevel, int playerLevel, int distance, int levelGap, int aggroRange, int protectMaxLevel)
    => monsterLevel - playerLevel >= levelGap && distance <= aggroRange && playerLevel <= protectMaxLevel;
```

### How it works (inlining, not calling)

There is **no call** in the lowered IR — the method body is **inlined** at each call site. The generator:

1. Resolves the invoked symbol and checks for `[KernelMethod]`
   ([SafeIrKernelMethodInliner](../../../src/SafeIR.PluginAnalyzer/Analysis/Lowering/Expressions/SafeIrKernelMethodInliner.cs)).
   A method without the attribute returns `null` so the normal dispatch continues; once the attribute is
   seen the inliner *owns* the call and any unsupported shape throws `NotSupportedException` (fail-safe).
2. Lowers each call-site argument in the **calling** context (so a `[HostBinding]` call or
   `[Capability]`-gated read passed as an argument still contributes its capability to the kernel's
   manifest).
3. Lowers the method's body with each parameter name bound to its already-lowered argument IR — the same
   compile-time substitution the `Select` projection uses (`ProjectedElement`), generalized to N
   parameters via `SafeIrExpressionLoweringContext.InlinedBindings` and resolved first in
   `LowerIdentifier` (so a parameter correctly shadows a same-named live setting).
4. Switches to the method body's own semantic model (the body may live in another file/tree).

The result is identical to writing the body inline, so it composes with everything the lambda lowering
already supports (AND-composed `Where`s, `Select` projection, the terminal `Send`).

### Where it plugs in

`SafeIrInvocationExpressionLowerer.Lower` tries, in order: `[HostBinding]` → `[KernelMethod]` →
`string.Equals`/`Substring` → throw. So the inliner sits next to the existing host-binding lowerer and
benefits from the same `catch (NotSupportedException)` fail-safe in `HookChainModelFactory` /
`PluginKernelModelFactory` (no package emitted; the runtime terminal throws `SGP062`).

### Constraints (verified at generation time; violation fails safe)

- The method must be **`static`**.
- Its body must be an **expression body** or a **single `return` statement** (no locals/loops — those are
  what the kernel-RPC statement lowerer in §2 adds for the batch-entrypoint shape).
- Parameters and return must be **supported scalars** (`bool`, `int`, `long`, `double`, `string`) — the
  same scalar set the rest of the lowering pipeline uses. A host-service interface cannot be passed as a
  parameter; call the `[HostBinding]` directly and pass its scalar result.
- **No recursion** — a `[KernelMethod]` inlining itself (directly or transitively) is rejected via an
  inline-stack guard.

### Incrementality note

Like the existing `[HostBinding]` lowering, the inliner reaches across syntax trees through the semantic
model. A full `dotnet build` always regenerates correctly; in rare IDE incremental-refresh cases editing
only a `[KernelMethod]` body in a *different* file than the call site may not re-lower until the next
build. This matches the host-binding precedent.

### Example

`GuardianKernel.ShouldHandle` ([example](../../../examples/GameServer/SafeIR.Game.Plugin/Kernels/GuardianKernel.cs))
factors its "is this monster bullying a weaker player in range?" rule into
`IsBullyingLowLevelPlayer([KernelMethod])`, passing the live settings as arguments. The example runs
end-to-end unchanged (exit 0; damage/tick drops from baseline exactly as before).

### Tests

[`PluginAnalyzerKernelMethodTests`](../../../tests/SafeIR.Tests/PluginAnalyzer/Generated/PluginAnalyzerKernelMethodTests.cs):
inlining into a kernel-class `ShouldHandle` (runtime gate equivalence), into an inline `Where` chain,
multi-argument helpers, capability collection through a `[HostBinding]` argument (manifest + install
under a wildcard grant), and the multi-statement-body fail-safe (no chain package generated).

---

## 2. Kernel RPC services

### Goal

Let a plugin ship a **batch operation** that runs server-side in a single roundtrip, so a loop over many
entities executes on the server (calling the host's existing bindings) instead of one network call per
entity — and so it can take and return **complex objects and lists of objects**. The motivating shape:

```csharp
await server.RegisterKernelRpcService<IMonsterKillerService, MonsterKillerKernel>();
List<KillResult> killed = server.KernelRpcService<IMonsterKillerService>().KillMonsters(ids); // one roundtrip

public interface IMonsterKillerService { List<KillResult> KillMonsters(List<int> monsterIds); }
public readonly record struct KillResult(int MonsterId, bool Success);

[KernelRpcService("monster-killer")]
public sealed partial class MonsterKillerKernel
{
    public List<KillResult> KillMonsters(List<int> monsterIds, HookContext ctx)
    {
        var results = new List<KillResult>();
        foreach (var id in monsterIds)
            results.Add(new KillResult(id, ctx.Host<IGameWorld>().Kill(id)));
        return results;
    }
}
```

### Why it is a sandboxed kernel (not trusted RPC)

In the target deployment the **server is frozen at release**; only plugins change. So the batch logic
must be shipped by the plugin and run on the server — and to be safe that means **verified, sandboxed
IR** (the same trust model as event kernels), calling only the host bindings the server already exposes.
A bespoke trusted RPC endpoint per plugin is impossible (the server cannot be recompiled).

### The record type — complex objects in the sandbox

Complex objects required a new composite **`Record`** type in the IR (§ "IR record/object type" commit):
`SandboxType.Record([fieldTypes])` + `RecordValue` + the `record.new`/`record.get` intrinsics, threaded
through the interpreter, validator, value validation, shape metering, canonical hashing, and JSON. A
record is a **positional** tuple; field *names* live in the analyzer and the host marshaling layer, which
map a C# DTO's members to record fields by **declaration order**. `List<Record>` is a list of objects.

Records run under both execution modes. The **interpreter** is the reference (validation, capabilities,
resource limits); the **compiler** emits fast, verifier-checked IL for `record.new`/`record.get` —
`EmitSandboxType` builds the field-type array through the trusted `CompiledRuntime.CreateTypeArray`
facade (like the value-array path), so the verifier's `newarr` restriction (SandboxValue only) is
unchanged, and the allowlist simply gains `RecordNew`/`RecordGet`/`TypeRecord`/`CreateTypeArray`.
`PluginServer.InstallRpcAsync` honors the server's execution mode (Auto/Compiled), so RPC kernels
compile to IL like event kernels.

### The pieces

| Layer | What it does |
|---|---|
| **Authoring** ([`[KernelRpcService]`](../../../src/SafeIR.Server.Abstractions/Contracts.cs)) | Marks the class; its one public batch method (trailing `HookContext` = host-binding marker) is the entrypoint. |
| **Lowering** ([`RpcKernelModelFactory`](../../../src/SafeIR.PluginAnalyzer/Analysis/Rpc/RpcKernelModelFactory.cs), [`SafeIrRpcJsonLowerer`](../../../src/SafeIR.PluginAnalyzer/Analysis/Rpc/SafeIrRpcJsonLowerer.cs)) | Lowers the method body to verified IR **JSON** and emits a `<Name>PluginPackage` whose `Create()` imports it — so it ships exactly like an event kernel. Supports locals, a `foreach` over a list, `if`/`else`, host bindings, DTO construction (`new T(...)`/`new T{...}` → `record.new`), list accumulation (`list.Add` → `list.add`), indexing/`.Count`, and `return`. |
| **Package + install** ([`PluginManifest.RpcEntrypoint`](../../../src/SafeIR.Plugins/PluginManifest.cs), [`RpcKernelPackageValidator`](../../../src/SafeIR.Plugins/Runtime/Rpc/RpcKernelPackageValidator.cs)) | A distinct package shape (no event subscription/contract) validated and installed via `InstallRpcAsync` / `PluginSession.InstallRpcAsync` (owned + revoked on disconnect, like event kernels). |
| **Invoke** ([`InstalledKernel.InvokeRpcAsync`](../../../src/SafeIR.Plugins/Kernel/InstalledKernel.Rpc.cs)) | Binds caller args to the entrypoint's leading parameters (live settings fill the trailing ones), runs the IR once under the execution gate, and **returns** the result value (not discarded). |
| **Typed surface** ([`KernelRpcMarshaller`](../../../src/SafeIR.Plugins/Runtime/Rpc/KernelRpcMarshaller.cs), [`KernelRpcServiceProxy`](../../../src/SafeIR.Plugins/Runtime/Rpc/KernelRpcServiceProxy.cs), [`PluginServer.RpcService`](../../../src/SafeIR.Plugins/Runtime/Rpc/PluginServer.Rpc.cs)) | `RegisterRpcServiceAsync<TService, TKernel>()` installs the kernel and binds the contract; `RpcService<TService>()` returns a runtime proxy that marshals C# args ↔ sandbox values (scalars, lists, DTOs, lists of DTOs) so `service.KillMonsters(ids)` returns real `List<KillResult>`. |

### Capabilities and effects

Host bindings called in the body contribute their capability and effects to the manifest exactly as in
event kernels (deny-at-install if the policy lacks them); the manifest declares `Cpu` + `Alloc` (when it
builds lists/records) + the binding effects, matched against the verified entrypoint.

### Constraints / current scope

- Runs interpreted **or compiled** — record IR compiles to verified IL (the GameServer example installs
  its kernels in `ExecutionMode.Compiled`).
- One batch method per service; parameters/return/DTO fields use the supported scalars, lists, or nested
  DTOs. DTO fields map by **declaration order** (positional records → constructor-parameter order).
- The typed proxy supports synchronous, `Task<T>`, and `ValueTask<T>` return shapes.
- **Over IPC:** the in-process `RegisterRpcServiceAsync`/`RpcService<T>` surface is the model; a remote
  facade forwards `InstallRpcAsync` + an invoke call (marshaling args/result) over the existing control
  service — mechanically identical to the GameServer event-kernel plumbing.

### Tests

[`RpcKernelRuntimeTests`](../../../tests/SafeIR.Tests/Plugins/Rpc/RpcKernelRuntimeTests.cs) (loop →
host binding per element → `List<Record>` in one roundtrip; JSON round-trip; capability deny;
arg-count guard), [`RpcKernelGenerationTests`](../../../tests/SafeIR.Tests/Plugins/Rpc/RpcKernelGenerationTests.cs)
(plain-C# `[KernelRpcService]` → generated IR → install → invoke), and
[`KernelRpcServiceProxyTests`](../../../tests/SafeIR.Tests/Plugins/Rpc/KernelRpcServiceProxyTests.cs)
(the typed proxy + `RegisterRpcServiceAsync`/`RpcService` + DTO/list marshaller round-trips), plus the
record-type foundation in [`SafeRecordCollectionTests`](../../../tests/SafeIR.Tests/Collections/SafeRecordCollectionTests.cs).

---

## 3. Compiling SafeIR to fast, verified IL

The record/RPC work above made every IR shape (records included) compile to verified IL. A follow-up pass
made that compiled IL **fast** without weakening any safety invariant.

### What was slow

- **Per-op boxing.** Every scalar operation allocated a `SandboxValue` (a heap `record`). A tight integer
  loop allocated several objects per iteration.
- **A hidden closure per arithmetic op.** `SandboxInt32Math.Add/Subtract/Multiply/Negate` were written as
  `Checked(() => checked(...))` — each call allocated a `Func<int>` *and* the `try/catch` blocked JIT
  inlining. This penalized the interpreter too.
- **A non-inlined metering call chain.** Each fuel charge went `CompiledRuntime.ChargeFuel` →
  `SandboxContext.ChargeFuel` → `ResourceMeter.ChargeFuel`, none inlined across assemblies.

### What changed

- **Unboxed I32 fast path** (`MethodEmitter`). Raw `int` flows on the IL stack for I32 literals, locals,
  and `+ - * / %`; values are boxed/unboxed only at boundaries (binding/function args, comparisons,
  return). Overflow/divide semantics are preserved by `*I32Raw` facades over the same `SandboxInt32Math`.
  Comparisons and all non-I32 types stay boxed so the stack-type verifier sees well-typed values.
- **Branchless `SandboxInt32Math`.** Overflow is detected with bit tests — no closures, no `try/catch`,
  allocation-free and inlineable. Identical `InvalidInput` errors.
- **`AggressiveInlining`** on the metering chain, the scalar box/unbox conversions, and the raw arithmetic
  so the JIT collapses them into the generated code.

### Fuel transparency (why it is safe)

Unboxing is **fuel-transparent**: compiled fuel is identical to the all-boxed path. Scalar box/unbox
coercions are O(1) and verifier-classified as **non-metered work**
(`GeneratedMethodShapeSignatures.IsScalarConversionCall`) — the same treatment literal construction already
gets. The per-op fuel/loop metering, the instruction-sparsity bound (<=32 instructions between meters), the
stack-type checks, and the per-work-call meter-density rule all still hold; `Verifier*` and golden tests
prove the shape and fuel are unchanged.

### Result and the floor

Worst-case probe (`dotnet run -c Release -- --probe-compiled`, a 20M-iteration `total = (total + i) % N`
loop): handwritten **1.0x**, compiled IL **~8x**, interpreted **~55x** — down from ~22x compiled before this
pass. The residual gap is **mandatory per-operation safety metering** (fuel + loop-iteration + amortized
deadline checks): a two-op loop body still carries ~6 fuel charges per iteration. That count is fixed by
the interpreter/compiled fuel-parity contract and the sandbox safety model, so it cannot be reduced without
weakening those guarantees. For realistic plugin bodies (conditionals, bindings, real work) the fixed
metering is amortized and compiled runs much closer to handwritten; the trivial loop is the deliberate
worst case where metering dominates.
