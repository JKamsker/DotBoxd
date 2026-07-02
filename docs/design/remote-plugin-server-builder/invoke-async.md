# InvokeAsync — inline-kernel and explicit capture bag (deep dive)

This document specifies how `server.InvokeAsync(lambda)` is detected, lowered to verified sandboxed
IR at compile time, shipped over async IPC, and executed server-side. It also documents both capture paths:
an explicit mutable capture bag, and an implicit-capture convenience path whose generated reflection fallback
can be statically rewritten by the DotBoxD Fody weaver. The server never compiles plugin source; only
verified IR crosses the boundary.

It corrects the reviewed designs where they were unsound and states every deferral explicitly.

---

## 1. Target shape

No-capture inline kernel:

```csharp
var monsterHealth = await server.InvokeAsync(async (IGameWorldAccess world) =>
{
    // Runs INSIDE the kernel (server-side, sandboxed verified IR).
    var monster = world.GetMonster("monster-2");
    return monster.Health;
});
```

Explicit capture-bag sync-in/out:

```csharp
var capture = new MonsterProbeCapture { MonsterId = "monster-2" };
var monsterName = await server.InvokeAsync(
    capture,
    async (IGameWorldAccess world, MonsterProbeCapture bag) =>
    {
        var monster = world.GetMonster(bag.MonsterId);
        bag.LastHealth = monster.Health;
        return monster.Name;
    });
```

The lambda body is lowered to the same verified IR a `[ServerExtension]` method produces. The explicit bag
is encoded as one record argument, and assigned bag properties are returned in a response record and written
back to the same object after the await.

Implicit capture convenience:

```csharp
var monsterId = "monster-2";
var lastHealth = 0;
var monsterName = await server.InvokeAsync(async (IGameWorldAccess world) =>
{
    var monster = world.GetMonster(monsterId);
    lastHealth = monster.Health;
    return monster.Name;
});
```

Implicit captures are emitted as `lambda.Target` reflection in the generated interceptor. When the
`DotBoxD.Plugins.Fody` weaver runs and can prove the compiler-generated closure shape, it rewrites those
helper calls to direct closure-field load/store IL. This remains a convenience path; the explicit capture-bag
overload is the source-level, compiler-stable path.

### Object snapshot surface

The implemented object surface is the flat host binding `world.GetMonster(id)`, returning
`MonsterSnapshot(string Id, string Name, int Health, int Level, int Position)`. Member access such as
`monster.Health` lowers to `record.get` by positional field order. The nested spelling
`world.Monsters.Get(id)` remains an ergonomic alias option; the verified binding is flat.

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
receiver's type and returns `null` unless it is a generated plugin server facade or generated server
interface**. Calls through the erased `IPluginServer<TWorld>` surface are rejected with a diagnostic because
the generated interceptor needs the concrete generated server receiver. Name alone is not a sufficient
discriminator.

### Lambda shape validation

Accept only:
- either a single explicitly-typed `IGameWorldAccess` lambda parameter, with no captures or supported
  reflection-backed local/parameter captures, or
- a mutable capture object argument plus a two-parameter lambda
  `(IGameWorldAccess world, TCaptures captures)`, where `TCaptures` is a supported record-shaped DTO, and
- a **block body** (expression-body lambdas are out of scope).

Wrong DotBoxD receiver shapes produce a build diagnostic. Unrelated methods named `InvokeAsync` return
`null` from the generator pipeline and are ignored.

---

## 3. Capture analysis

Use Roslyn data-flow:

```csharp
var flow = semanticModel.AnalyzeDataFlow(lambdaBlock);
```

- Lambda-only calls with no ambient captures lower to a zero-argument anonymous kernel.
- Lambda-only calls with ambient local/parameter captures lower those captured symbols to IR parameters named
  after the source symbols. The generated interceptor reads and writes the compiler closure fields by source
  symbol name via `lambda.Target` reflection, and the optional Fody weaver rewrites those helper calls to
  static field access when the compiled call site exposes a safe compiler-generated display class.
- Capture-bag calls also reject ambient `DataFlowsIn` / `DataFlowsOut`; only the explicit bag parameter can
  carry values across the boundary.
- **Capture-bag sync-in** = the capture-bag object encoded as a server-extension wire record.
- **Capture-bag sync-out** = simple assignments to supported settable bag properties, e.g. `bag.LastHealth = ...`.
- **Implicit sync-in** = captured local/parameter values encoded as individual server-extension wire arguments.
- **Implicit sync-out** = captured locals/parameters written inside the lambda and returned in the response
  record, then reflected back into the closure object.

Each assigned bag property gets an initialized IR local (`__syncOut_<Property>`), and returns read those
locals back through the response envelope.

Nullable-reference capture-bag fields are a documented caveat. The IR scalar type system cannot represent
"String-or-null": the server-extension string wire encoding coerces null to empty, and wire-to-sandbox
conversion validates each value's kind against the IR-declared `SandboxType`. Capture bags should use non-nullable fields
or accept null-to-empty-string behavior.

---

## 4. IR function shape

```
function "$anon:<hex>" () -> <return>
function "$anon:<hex>" (<implicit captures>) -> <return or Record([returnValue, syncOut0, ...])>
function "$anon:<hex>" (captures: Record([...])) -> Record([returnValue, syncOut0, ...])
```

- The lambda's `IGameWorldAccess world` parameter is **not** an IR parameter — calls on it
  (`world.GetMonster(id)`) lower to a host-binding call via the existing host-binding lowerer.
- For capture-bag calls, the bag lambda parameter is the single IR parameter. Reads like `bag.MonsterId`
  lower to `record.get(Var("bag"), 0)`.
- For implicit captures, captured locals/parameters are IR parameters using the original source symbol names.
  Reads like `monsterId` lower to `Var("monsterId")`, and assignments like `lastHealth = ...` lower to
  `set lastHealth`.

### Return type

- **No sync-out:** the IR return type is the lowered lambda return type directly.
- **With sync-out:** the IR return type is `Record([returnValue, syncOut0, …])`.

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

1. Building the optional single record-shaped capture-bag parameter.
2. Declaring leading IR locals for assigned bag properties, initialized from the inbound bag.
3. Overriding simple assignments to bag properties so `bag.LastHealth = expr` lowers to
   `set __syncOut_LastHealth`.
4. Building reflection-backed implicit capture parameters when lambda-only calls capture locals/parameters.
5. For sync-out: synthesizing `return record.new([userReturnExpr, syncOut0, …])`. This is done structurally
   for each lowered return path; the implementation does not scan or post-process JSON text.

---

## 6. Capture marshalling (the central mechanism)

A C# interceptor's non-receiver parameters must match the intercepted method's argument list **exactly** —
it **cannot** add `out`/`ref` parameters or change the return type (confirmed by the existing
`DotBoxDHookChainInterceptorEmitter`, whose interceptor returns `HookPipeline<TEvent>` and forwards the
identical handler). Therefore captures cannot be threaded as extra interceptor parameters, and
direct caller-local access is impossible.

Implementation finding: the call-site-local mechanism above is not valid C#. A generated interceptor method
body is compiled as a normal generated method body; it cannot reference locals from the intercepted caller's
lexical scope by name.

The stable capture path is an explicit mutable capture bag:

```csharp
var bag = new MonsterProbeCapture { MonsterId = "monster-2" };
var name = await server.InvokeAsync(
    bag,
    async (IGameWorldAccess world, MonsterProbeCapture captures) =>
    {
        var monster = world.GetMonster(captures.MonsterId);
        captures.LastHealth = monster.Health;
        return monster.Name;
    });
```

The bag is encoded as one server-extension wire record argument (sync-in). Assignments to bag properties lower to
generated IR locals. If any bag property is assigned, the anonymous kernel returns
`Record([returnValue, syncOut0, …])`; the interceptor decodes the response and writes each sync-out value
back onto the same bag object after the await. This is more explicit than closure-local capture, but it is
reflection-free, compiler-stable, and works with the interceptor parameter-shape rules.

The convenience capture path uses closure-`Target` reflection. The generator uses Roslyn data-flow to find
captured locals/parameters, emits IR parameters with their source symbol names, and emits interceptor helpers
that read/write fields with those names from `lambda.Target`. If the compiler does not expose a matching
closure field, the interceptor throws a clear `NotSupportedException` and the caller should switch to the
explicit capture bag.

When `DotBoxD.Plugins.Fody` is enabled, it post-processes the compiled assembly after C# lowering:

1. Find calls into the generated `DotBoxD.Plugins.Generated.InvokeAsyncInterceptors.InvokeAsync_*` methods.
2. Read the delegate construction IL feeding each interceptor call to discover the compiler display-class
   type for that source location.
3. Find the generated async state machine for that interceptor.
4. Replace `__ReadCapture(lambda, "field")` and `__WriteCapture(lambda, "field", value)` calls in
   `MoveNext` with `lambda.Target`, a cast to the display class, and direct `ldfld` / `stfld` instructions.

The weaver widens only the compiler-generated display class from private nested visibility to assembly
visibility when needed. If any proof step fails, or if a field/type shape is not safely accessible, it leaves
the original helper call in place, so the reflection fallback continues to work.

### Generated interceptor (no-capture)

```csharp
[InterceptsLocation(version, "<data>")]
internal static async ValueTask<int> InvokeAsync_0(
    this GamePluginServer server,
    Func<IGameWorldAccess, ValueTask<int>> lambda)   // matches the original signature exactly
{
    ArgumentNullException.ThrowIfNull(lambda);
    var __pluginId = await server.Services
        .EnsureAnonymousKernelAsync("$anon:<hex>", global::…$anon_<hex>PluginPackage.Create)
        .ConfigureAwait(false);

    var __request  = WriteServerExtensionArguments(Array.Empty<object>());
    var __response = await server.Services.WireClient
        .InvokeServerExtensionAsync(__pluginId, __request).ConfigureAwait(false);
    var __result   = ReadServerExtensionValue(__response);

    return __result.RequireInt32();
}
```

### Capture-bag sync-out addition

The response is a `Record([returnValue, syncOut0, …])`. The interceptor splits it, assigns each sync-out
field back to the caller-provided bag object, then returns the decoded return value.

### Implicit-capture sync-out addition

The response shape is the same `Record([returnValue, syncOut0, …])`. The interceptor splits it, writes each
sync-out field back through the generated `__WriteCapture(lambda, "<symbol>", value)` helper, then returns the
decoded return value.

---

## 7. Anonymous-kernel install — identity, caching, concurrency

Generated plugin server facades expose a generated `Services` accessor. Anonymous `InvokeAsync` kernels use
that accessor's server-extension install and invoke path:

```csharp
internal IServerExtensionWireClient WireClient => _control;

private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _anonymousKernels = new(StringComparer.Ordinal);

public Task<string> EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> packageFactory)
{
    var install = _anonymousKernels.GetOrAdd(
        pluginId,
        id => new Lazy<Task<string>>(() => InstallServerExtensionPackageAsync(packageFactory()).AsTask()));
    return AwaitAnonymousKernelAsync(pluginId, install);
}

private async Task<string> AwaitAnonymousKernelAsync(string pluginId, Lazy<Task<string>> install)
{
    try
    {
        return await install.Value.ConfigureAwait(false);
    }
    catch
    {
        ((ICollection<KeyValuePair<string, Lazy<Task<string>>>>)_anonymousKernels)
            .Remove(new KeyValuePair<string, Lazy<Task<string>>>(pluginId, install));
        throw;
    }
}

private ValueTask<string> InstallServerExtensionPackageAsync(PluginPackage package)
    => RequireControl().InstallServerExtensionAsync(PluginPackageJsonSerializer.Export(package));
```

- **Install-once-per-id-per-connection.** The `ConcurrentDictionary<string, Lazy<Task<string>>>` `GetOrAdd`
  ensures concurrent first-calls share a single install task. A naive check-then-install races: two installs
  of the same `$anon:` id trigger the same-owner reinstall guard (`KernelRegistry.Add`, DBXK060), which
  **replaces and revokes** the incumbent — cancelling an in-flight invoke's execution gate. The concurrency-safe
  cache is **mandatory**, not an optimization.
- **Failed installs are retryable.** If the shared install task faults or is cancelled, the generated facade removes
  that exact lazy value from `_anonymousKernels` before rethrowing. The next call for the same anonymous plugin id
  creates a fresh install attempt instead of reusing a permanently faulted task.
- **Per-connection cache.** The generated server facade is constructed fresh per connection, so the cache
  clears naturally on reconnect; the new session re-installs and the old one is revoked on disconnect.
- **Throwing stub overload.** The generated facade declares
  `public ValueTask<TReturn> InvokeAsync<TReturn>(Func<IGameWorldAccess, ValueTask<TReturn>> lambda) => throw …;`
  so an un-intercepted call (interceptors opt-in missing, or `GetInterceptableLocation` returned null) fails
  loudly. The generator also emits a build diagnostic when the call site is not interceptable.

---

## 8. Server-side execution

Anonymous kernels reuse the server-extension RPC path:

1. `InstallServerExtensionAsync(json)` → `PluginPackageJsonSerializer.Import` → `ServerPolicy.ForKernel(manifest
   .RequiredCapabilities)` → `PluginSession.InstallServerExtensionAsync` → `PluginServer.InstallServerExtensionAsync` →
   `RpcKernelPackageValidator.Validate` → `SandboxHost.PrepareAsync` (capability deny-at-install via
   `PolicyResolver.Validate`) → `RpcKernelPackageValidator.ValidatePrepared` → `new InstalledKernel(...)`
   owned by the session.
2. `InvokeServerExtensionAsync(pluginId, bytes)` → decode arguments → per-arg wire-to-sandbox conversion
   against each `function.Parameters[i].Type` → `InstalledKernel.InvokeServerExtensionAsync` →
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

## 9. Sync-out wire envelope

The response must carry mutated capture-bag fields alongside the return value. The implemented carrier is a
single `Record` over the existing `InvokeServerExtensionAsync` method:

- no sync-out: the response is the user return value directly;
- with sync-out: the response is `Record([returnValue, syncOut0, …])`.

The generated interceptor knows the expected field count from the source-generated capture shape and checks
it before writing sync-out values back to the bag. This needs no manifest metadata, no new IPC method, and no
new binary codec.

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

- **Phase 2:** detection + receiver guard + lambda-shape validation + no-capture body lowering + anonymous
  package + interceptor + concurrency-safe install + attribute dedup.
- **Phase 2b:** reflection-backed implicit local/parameter captures on the lambda-only overload.
- **Phase 3:** explicit mutable capture-bag sync-in/out via record argument plus response record envelope.
- **Phase 4:** object-returning host binding via flat `world.GetMonster(id)`, Record-typed
  `BindingDescriptor`, and member access through `record.get`.

## 12. What is reused vs new

**Reused as-is:** `HookChainIdentity.Compute`; `DotBoxDRpcJsonLowerer.LowerBody`/`LowerInvocation`/
`LowerMemberAccess`; `DotBoxDHostBindingExpressionLowerer` (capability sink); `DotBoxDRpcTypeMapper.JsonType`;
server-extension binary argument/value encoding; server-extension wire-to-sandbox conversion; `InstalledKernel
.InvokeServerExtensionAsync` + `BuildRpcInput`; `PluginSession`/`PluginServer` install + ownership + revocation;
`SandboxHost.PrepareAsync` + `PolicyResolver` capability gating; `RpcKernelPackageValidator`.

**New:** `InvokeAsyncModelFactory`, `InvokeAsyncCallShape`, `InvokeAsyncInterceptorEmitter`, shared
`InterceptsLocationAttributeEmitter`, the fourth generator pipeline, generated facade members
(`InvokeAsync<TReturn>` stub, capture-bag `InvokeAsync<TCaptures,TReturn>` stub, `Services.WireClient`,
`Services.EnsureAnonymousKernelAsync`), reflection capture helpers in generated interceptors,
`DotBoxD.Plugins.Fody` for safe static implicit-capture rewriting, and the sample `world.GetMonster(id)`
snapshot binding.
