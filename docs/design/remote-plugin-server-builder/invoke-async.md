# InvokeAsync — closure-capture inline-kernel (deep dive)

This document specifies how `server.Kernels.InvokeAsync(lambda)` is detected, lowered to verified sandboxed
IR at compile time, shipped over async IPC, executed server-side, and how closed-over locals are synced in
and out — while preserving the DotBoxD invariant (the server never compiles plugin source; only verified IR
crosses the boundary).

It corrects the reviewed designs where they were unsound and states every deferral explicitly.

---

## 1. Target shape

```csharp
string firstMonsterName = "monster-1";

var monsterResults = await server.Kernels.InvokeAsync((IGameWorldAccess world) =>
{
    // Runs INSIDE the kernel (server-side, sandboxed verified IR).
    var h3 = world.GetHealth("monster-3");
    var l3 = world.GetLevel("monster-3");
    var h4 = world.GetHealth("monster-4");
    var l4 = world.GetLevel("monster-4");

    return new MonsterDto[]
    {
        new MonsterDto("monster-3", h3, l3),
        new MonsterDto("monster-4", h4, l4),
    };
});
```

Captured locals **read** inside the lambda become kernel inputs (sync-in). Captured locals **assigned**
inside the lambda are written back into the caller's closure after the await (sync-out). The lambda body is
lowered to the same verified IR a `[KernelRpcService]` method produces.

### v1 surface compromise (flat bindings)

The literal `world.Monsters.Get(id).Name` object surface from the feature spec is **out of v1 scope**
(Phase 4). `DotBoxDRpcJsonLowerer.LowerInvocation` accepts only `[HostBinding]`-annotated methods called
directly on the lambda parameter, and `DotBoxDHostBindingExpressionLowerer` rejects non-scalar return types
(`DotBoxDTypeNameReader.SandboxTypeName` → `"unsupported"`). v1 uses the six flat scalar bindings
(`GetHealth`, `GetLevel`, `GetPosition`, `IsMonster`, `KillMonster`, `GetThreat`) and the kernel body
assembles any DTO from them — exactly what `MonsterKillerKernel` already does. This needs **no** new
binding, lowerer, or IPC change.

---

## 2. Detection (fourth generator pipeline)

Mirror the existing `InvokeKernel` pipeline in `PluginPackageGenerator.Initialize`:

```csharp
var invokeAsyncResults = context.SyntaxProvider
    .CreateSyntaxProvider(
        static (node, _) => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "InvokeAsync" }
        },
        static (ctx, ct) => InvokeAsyncModelFactory.Create(ctx, ct))
    .Where(static r => r is not null)
    .Select(static (r, _) => r!);
```

The predicate is allocation-free and name-only. `InvokeAsyncModelFactory.Create` then **resolves the
receiver's type and returns `null` unless it is the kernel-invocation surface**
(`DotBoxDGenerationNames.Metadata.KernelInvocationSurfaceType` = the FQN of `RemoteKernelControl`), mirroring
`HookChainModelFactory`'s `HookPipeline<TEvent>` guard. Name alone is not a sufficient discriminator.

### Lambda shape validation

Accept only:
- a single, explicitly-typed parameter whose declared type is the host-access interface
  (`IGameWorldAccess`), and
- a **block body** (expression-body lambdas are out of scope — use `InvokeKernel` for those).

Anything else returns `null` (fail-safe, no output).

---

## 3. Closure-capture analysis

Use Roslyn data-flow:

```csharp
var flow = semanticModel.AnalyzeDataFlow(lambdaBlock);
```

- **Sync-in** = `flow.DataFlowsIn` ∩ (`ILocalSymbol` | `IParameterSymbol`) from the enclosing scope, minus
  the lambda's own parameter. Each capture's type must be supported by `DotBoxDRpcTypeMapper.JsonType`;
  unsupported types fail safe.
- **Sync-out** = `flow.WrittenInside` ∩ enclosing-scope locals, minus the lambda's parameter and minus
  locals declared inside the lambda.

A capture that is **both** read and written is a sync-in parameter that the IR body reassigns; a
**write-only** capture has no leading parameter and needs a dedicated IR local slot.

**Nullable-reference captures are rejected in v1** with a clear diagnostic. The IR scalar type system cannot
represent "String-or-null": `KernelRpcValue.String(null)` coerces to empty, and
`KernelRpcValueConverter.ToSandboxValue` validates each value's kind against the IR-declared `SandboxType`,
so a `string` IR parameter cannot receive a `Unit`-kind null sentinel. The spec example must use a
non-nullable captured `string` (e.g. `firstMonsterName`), or accept that null becomes empty-string with a
documented caveat.

---

## 4. IR function shape

```
function "$anon:<hex>" (<sync-in captures, original names, in DataFlowsIn order>) -> <return>
```

- The lambda's `IGameWorldAccess world` parameter is **not** an IR parameter — calls on it
  (`world.GetHealth(id)`) lower to `CallExpression("host.world.getHealth", …)` via the existing host-binding
  lowerer.
- **Captured-local reads must resolve to the IR parameters.** `DotBoxDRpcJsonLowerer.Expressions.cs`
  resolves every `IdentifierNameSyntax` unconditionally to `Var(name)` with **no rename hook**. Therefore
  the IR parameters use the **original capture names** (no `__cap_` prefix). The earlier "rename adapter
  over `LowerBody`" is fictional and is not used. The accepted trade-off: a kernel-local must not shadow a
  capture name (acceptable; can be enforced with a diagnostic if needed).

### Return type

- **No sync-out (Phase 2):** the IR return type is the lowered lambda return type directly
  (e.g. `List<Record(string,int,int)>` for `MonsterDto[]`).
- **With sync-out (Phase 3):** the IR return type is `Record([syncOut0, …, syncOutK-1, returnValue])`.

### Manifest / package JSON

Constructed exactly like `RpcKernelModelFactory.EmitPackage`:
`pluginId = "$anon:" + HookChainIdentity.Compute(invocation)` (FNV-1a of file path + span start; verified to
pass `ValidateText` and the descriptor guards — a colon and a hex run are not forbidden), `mode=Auto`,
`liveSettings=[]`, `subscriptions=[]`, `rpcEntrypoint`=the function id, `requiredCapabilities`=the
host-binding capability sink, `effects`=`Cpu` (+`Alloc` when the lowerer allocates). The generator emits
`module.id == pluginId` **and** `module.metadata.pluginId == pluginId` identically (the existing RPC factory
already emits `"metadata":{"kernel":…,"pluginId":…}`).

The package is emitted as a generated `…$anon_<hex>PluginPackage.Create()` static whose body is
`PluginPackageJsonSerializer.Import("<json literal>")` — identical structure to every other generated
package. No runtime package resolution.

---

## 5. Body lowering — honest reuse boundary

`DotBoxDRpcJsonLowerer.LowerBody(block)` lowers the body **statements** unchanged: locals, assignments,
`foreach`, `if`/`else`, `record.new`, `list.Add`, `return <expr>`. What is **net-new** in
`InvokeAsyncModelFactory` (not "reuse"):

1. Building the IR parameter list from the sync-in captures (original names, mapped types).
2. Declaring IR local slots for write-only sync-out captures.
3. For sync-out: synthesizing `return record.new([capture0, …, userReturnExpr])`. This is done
   **structurally, per return statement** — the factory lowers each return's value through the lowerer and
   wraps it. Bodies have **multiple** returns (inside `if`/`else`, nested blocks), so a single string scan
   for `{"op":"return","value":` is unsound and is **not** used. If a single-return v1 constraint is taken
   for schedule reasons, the factory rejects multi-return lambdas with a diagnostic instead.

A read-and-written sync-in capture is reassigned in the body via `set <name>` (the interpreter frame shares
one slot space across params and locals — `InterpreterFrame`), and its final value is read back into the
return record at the return site.

---

## 6. Capture marshalling (the central mechanism)

A C# interceptor's non-receiver parameters must match the intercepted method's argument list **exactly** —
it **cannot** add `out`/`ref` parameters or change the return type (confirmed by the existing
`DotBoxDHookChainInterceptorEmitter`, whose interceptor returns `HookPipeline<TEvent>` and forwards the
identical handler). Therefore captures cannot be threaded as extra interceptor parameters, and
**closure-`Target` reflection is rejected** (`GetField("name")` depends on Roslyn name-mangling, which is
not spec-guaranteed; the reviewed design even mis-guessed `"<lastMonsterName>i__Field"`).

Implementation finding: the call-site-local mechanism above is not valid C#. A generated interceptor method
body is compiled as a normal generated method body; it cannot reference locals from the intercepted caller's
lexical scope by name. The generator therefore rejects lambda-only calls that capture caller locals. That
keeps the lambda-only overload correct for no-capture inline kernels and avoids closure-field reflection.

The implemented capture path is an explicit mutable capture bag:

```csharp
var bag = new MonsterProbeCapture { MonsterId = "monster-2" };
var name = await server.Kernels.InvokeAsync(
    bag,
    (IGameWorldAccess world, MonsterProbeCapture captures) =>
    {
        var monster = world.GetMonster(captures.MonsterId);
        captures.LastHealth = monster.Health;
        return monster.Name;
    });
```

The bag is encoded as one `KernelRpcValue.Record` argument (sync-in). Assignments to bag properties lower to
generated IR locals. If any bag property is assigned, the anonymous kernel returns
`Record([returnValue, syncOut0, …])`; the interceptor decodes the response and writes each sync-out value
back onto the same bag object after the await. This is more explicit than closure-local capture, but it is
reflection-free, compiler-stable, and works with the interceptor parameter-shape rules.

### Generated interceptor (no-capture)

```csharp
[InterceptsLocation(version, "<data>")]
internal static async ValueTask<MonsterDto[]> InvokeAsync_0(
    this RemoteKernelControl kernels,
    Func<IGameWorldAccess, MonsterDto[]> __lambda)   // matches the original signature exactly
{
    var __pluginId = await kernels
        .EnsureAnonymousKernelAsync("$anon:<hex>", global::…$anon_<hex>PluginPackage.Create)
        .ConfigureAwait(false);

    var __request  = KernelRpcBinaryCodec.EncodeArguments(Array.Empty<KernelRpcValue>());
    var __response = await kernels.WireClient
        .InvokeKernelRpcAsync(__pluginId, __request).ConfigureAwait(false);
    var __result   = KernelRpcBinaryCodec.DecodeValue(__response);

    // decode List<Record(string,int,int)> -> MonsterDto[]
    return ReadMonsterDtoArray(__result);
}
```

### Capture-bag sync-out addition

The response is a `Record([returnValue, syncOut0, …])`. The interceptor splits it, assigns each sync-out
field back to the caller-provided bag object, then returns the decoded return value.

---

## 7. Anonymous-kernel install — identity, caching, concurrency

New members on `RemoteKernelControl`:

```csharp
internal IKernelRpcWireClient WireClient => _control;   // IGamePluginControlService implements InvokeKernelRpcAsync

private readonly ConcurrentDictionary<string, Task<string>> _anonymousKernels = new(StringComparer.Ordinal);

internal Task<string> EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> packageFactory)
    => _anonymousKernels.GetOrAdd(pluginId, static (id, args) => InstallOnceAsync(id, args.factory, args.control),
        (factory: packageFactory, control: _control));

private static async Task<string> InstallOnceAsync(string pluginId, Func<PluginPackage> factory, IGamePluginControlService control)
{
    var json = PluginPackageJsonSerializer.Export(factory());
    return await control.InstallKernelRpcAsync(json).ConfigureAwait(false);
}
```

- **Install-once-per-id-per-connection.** The `ConcurrentDictionary<string, Task<string>>` `GetOrAdd`
  ensures concurrent first-calls share a single install `Task`. A naive check-then-install races: two
  installs of the same `$anon:` id trigger the same-owner reinstall guard (`KernelRegistry.Add`, DBXK060),
  which **replaces and revokes** the incumbent — cancelling an in-flight invoke's execution gate. The
  concurrency-safe cache is **mandatory**, not an optimization.
- **Per-connection cache.** `RemoteKernelControl` is constructed fresh per connection
  (`new RemotePluginServer(...)`), so the cache clears naturally on reconnect; the new session re-installs
  and the old one is revoked on disconnect.
- **Throwing stub overload.** `RemoteKernelControl` declares
  `public ValueTask<TReturn> InvokeAsync<TReturn>(Func<IGameWorldAccess, TReturn> lambda) => throw …;` so an
  un-intercepted call (interceptors opt-in missing, or `GetInterceptableLocation` returned null) fails
  loudly. Ensure this is the **sole** `InvokeAsync` candidate at the call site to avoid overload-resolution
  mis-binding, and emit a build diagnostic when interception location is null.

---

## 8. Server-side execution

Anonymous kernels reuse the entire named-RPC path, unchanged:

1. `InstallKernelRpcAsync(json)` → `PluginPackageJsonSerializer.Import` → `ServerPolicy.ForKernel(manifest
   .RequiredCapabilities)` → `PluginSession.InstallRpcAsync` → `PluginServer.InstallRpcCoreAsync` →
   `RpcKernelPackageValidator.Validate` → `SandboxHost.PrepareAsync` (capability deny-at-install via
   `PolicyResolver.Validate`) → `RpcKernelPackageValidator.ValidatePrepared` → `new InstalledKernel(...)`
   owned by the session.
2. `InvokeKernelRpcAsync(pluginId, bytes)` → `DecodeArguments` → per-arg `KernelRpcValueConverter
   .ToSandboxValue` against each `function.Parameters[i].Type` → `InstalledKernel.InvokeRpcAsync` →
   `BuildRpcInput` → execute → return one `SandboxValue`.

### `BuildRpcInput` parameter shapes (must match the generated IR)

Verified in `InstalledKernel.Rpc.cs`:
- **0 captures** → 0-param entrypoint → input `SandboxValue.Unit`.
- **1 capture** → 1-param entrypoint → input is the **bare** `arguments[0]` (not a 1-element frame). The
  generated IR body must read the bare value.
- **N≥2 captures** → input is `SandboxValue.FromList(values, values[0].Type)` — a positional frame whose
  declared element type is the first capture's type. Heterogeneous captures round-trip correctly because
  each wire arg was already validated against its own IR parameter type before packing; the element-type
  tag is an internal positional-frame detail the IR destructures by index.

### Capability gating

`requiredCapabilities` are derived by `DotBoxDHostBindingExpressionLowerer` from the `[HostBinding]` calls in
the lambda body — the same sink used for named RPC kernels. `ServerPolicy.ForKernel` grants exactly the
matching namespaces; a lambda touching an ungranted binding fails at install. **No new policy
infrastructure.**

---

## 9. Sync-out wire envelope (Phase 3)

The response must carry mutated captures alongside the return value. The existing
`InvokeKernelRpcAsync` returns exactly one `KernelRpcValue` with **no envelope**. Two options:

- **Recommended: single `Record` over the existing IPC.** The IR entrypoint returns
  `Record([returnValue, syncOut0, …])` (or `[syncOut0, …, returnValue]`); the bare-value response already
  carries a `RecordValue`. The interceptor unpacks it. **No IPC contract change, no codec change.**
- **Alternative: dedicated `AnonResponseCodec` + new IPC method.** A frame `[varint syncOutCount][syncOut
  values…][returnValue]` via a new `InvokeAnonymousKernelRpcAsync(pluginId, byte[]) → byte[]`. This requires
  making `KernelRpcBinaryCodec.WriteLength`/`WriteValue`/`Reader` **internal** (currently private) so
  `AnonResponseCodec` reuses them. `AnonResponseCodec.Decode(ReadOnlyMemory<byte>)` drives the `ref struct
  Reader` by ref and **must call `EnsureConsumed()`** to keep the existing trailing-byte tamper guard.

### `syncOutCount` — where it lives

`PluginManifest` has **no** `Metadata` member (verified — the reviewed runtime design invented it). Two
carriers:
- **Field-count inference (recommended):** `syncOutCount = ((RecordValue)result).Fields.Count - 1`,
  validated at install by a new `DBXK073` shape check (anonymous entrypoint return type must be a Record
  with arity ≥ 1). No importer dependency.
- **Module metadata:** `module.metadata` is an open string→string dictionary the importer enumerates
  without a key whitelist (`JsonImporter.ReadMetadata`). Emit `"$anon.syncOutCount":"N"`; read via
  `kernel.Package.Module.Metadata["$anon.syncOutCount"]`.

---

## 10. Interceptor attribute dedup — hard prerequisite

`DotBoxDHookChainInterceptorEmitter.Emit` calls
`context.AddSource("DotBoxDInterceptsLocationAttribute.g.cs", AttributeSource)`. A second emitter adding the
same hint name **crashes the generator** whenever a compilation contains both a hook chain and an
`InvokeAsync`. Before wiring the `InvokeAsync` pipeline, extract a shared
`InterceptsLocationAttributeEmitter.EnsureEmitted`, driven by a combined `IncrementalValueProvider<bool>`
over both interception sets, that emits the attribute file exactly once. The hook-chain emitter is
refactored to call it.

---

## 11. Phasing summary

- **Phase 2:** detection + receiver guard + lambda-shape validation + sync-in capture analysis + body
  lowering + anonymous package + interceptor (sync-in only) + concurrency-safe install + attribute dedup +
  null-interception diagnostic. Flat world surface.
- **Phase 3:** sync-out (write-back captures, response Record/envelope, `DBXK073` shape validator,
  nullable-capture diagnostic, structural multi-return wrapping or single-return constraint).
- **Phase 4 (sequenced last; fully planned in `plan.md`):** object-returning host bindings
  (`world.Monsters.Get(id).Name`) — new `IGameWorldAccess` member, Record-typed `BindingDescriptor`, extended
  host-binding lowerer + member-access via `record.get`. It is the only phase that changes the host-binding
  type system, which is why it lands after Phases 1–3.

## 12. What is reused vs new

**Reused as-is:** `HookChainIdentity.Compute`; `DotBoxDRpcJsonLowerer.LowerBody`/`LowerInvocation`/
`LowerMemberAccess`; `DotBoxDHostBindingExpressionLowerer` (capability sink); `DotBoxDRpcTypeMapper.JsonType`;
`KernelRpcBinaryCodec` (encode args / decode value); `KernelRpcValueConverter`; `InstalledKernel
.InvokeRpcAsync` + `BuildRpcInput`; `PluginSession`/`PluginServer` install + ownership + revocation;
`SandboxHost.PrepareAsync` + `PolicyResolver` capability gating; `RpcKernelPackageValidator`.

**New:** `InvokeAsyncModelFactory`, `InvokeAsyncInterceptorEmitter`, shared
`InterceptsLocationAttributeEmitter`, the fourth generator pipeline,
`DotBoxDGenerationNames.Metadata.KernelInvocationSurfaceType`, `RemoteKernelControl` members
(`InvokeAsync<TReturn>` stub, `WireClient`, `EnsureAnonymousKernelAsync`), and (Phase 3, if the dedicated
path is taken) `AnonResponseCodec` + `InvokeAnonymousKernelRpcAsync` + `DBXK073`.
