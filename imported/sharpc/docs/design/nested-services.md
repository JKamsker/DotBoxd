# Nested Services in ShaRPC — Design

> **Historical design note.** This document predates the peer-model refactor. The
> `IShaRpcClient` / `ShaRpcServer` / `ShaRpcClientBuilder` types named below were superseded by
> the symmetric `RpcPeer` / `RpcHost` surface: the invoke contract is now `IRpcInvoker`
> (implemented by `RpcPeer`), inbound dispatch lives in `RpcPeerInboundDispatcher`, and generated
> proxies take an `IRpcInvoker`. The reasoning here still holds; only the type names changed.

## 1. Wire protocol additions

**Decision: Option B — add a new `InvokeOnInstanceAsync` overload AND a new `ServiceHandle` payload type.** Option A (name-mangling `IFoo@<guid>`) is rejected: it overloads `RpcRequest.ServiceName`, breaks the dispatcher registry lookup invariant (`TryGetValue(serviceName, ...)`), and forces every layer that logs / inspects service names to learn the mangling rule. Option B keeps the existing `RpcRequest` envelope alphabet-clean and isolates the new concern to a single field.

**New protocol fields.** Add one nullable string to `RpcRequest`:

```csharp
public sealed class RpcRequest
{
    public int MessageId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string? InstanceId { get; set; }   // NEW. null = singleton service call.
}
```

> The serialized method arguments are no longer carried inside this envelope; they travel as the
> frame's raw trailing payload (see the wire format in the API reference) so the dispatcher can read
> them as a zero-copy slice of the receive buffer.

`InstanceId` is `null` for all existing top-level service calls — the field is additive and wire-compatible because MessagePack/JSON tolerate unknown-but-absent properties.

**New return payload type** added to `ShaRPC.Core.Protocol`:

```csharp
public sealed record ServiceHandle(string ServiceName, string InstanceId);
```

When a dispatcher method's *return value* is itself a `[ShaRpcService]`, the generated dispatcher serializes a `ServiceHandle` instead of attempting to serialize the live object. When the client proxy sees a method whose declared return is a `[ShaRpcService]` interface, it deserializes the response payload as `ServiceHandle` and constructs a `SubServiceProxy(client, ServiceName, InstanceId)`.

**New invoke overload** on `IRpcInvoker` (the call surface implemented by `RpcPeer`):

```csharp
Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
    string service, string instanceId, string method, TRequest request, CancellationToken ct = default);
Task<TResponse> InvokeOnInstanceAsync<TResponse>(
    string service, string instanceId, string method, CancellationToken ct = default);
Task InvokeOnInstanceAsync<TRequest>(
    string service, string instanceId, string method, TRequest request, CancellationToken ct = default);
```

`RpcPeer`'s implementation forwards to the same outbound request path as `InvokeAsync` but sets `RpcRequest.InstanceId`.

## 2. Server-side instance registry

**Decision: per-connection, session-scoped, with explicit `Release` wire op.** Server-wide instance lookup would leak instances across tenants and require global GUID uniqueness; per-connection scoping matches the natural lifetime of a `SubService` (which conceptually represents "this client's view of subscope X") and lets the per-peer teardown (`RpcPeerInboundDispatcher.StopAsync`) drain everything on disconnect.

New abstraction in `ShaRPC.Core.Server`:

```csharp
public interface IInstanceRegistry
{
    string Register(string serviceName, object instance);
    bool TryGet(string serviceName, string instanceId, out object instance);
    void Release(string serviceName, string instanceId);
    void ReleaseAll();   // called on connection teardown
}
```

`InstanceId` is `Guid.NewGuid().ToString("N")` — opaque and unforgeable enough; not a security boundary on its own (combined with per-connection scope, an attacker on a different connection cannot reach another connection's instances).

**Lifetime.** Default behavior: instances live until the connection closes. A future "explicit release" message type (`MessageType.ReleaseInstance = 0x05`) can be sent by the client when its `SubServiceProxy` is disposed; if not sent (proxy was GC'd without dispose), the instance lingers until disconnect. Reference counting is rejected as out-of-scope — it requires coordinating proxy finalizers across the wire and inviting double-release races.

**Where it lives.** One `IInstanceRegistry` per peer (i.e. per duplex `IRpcChannel`). `RpcPeerInboundDispatcher` owns an `InstanceRegistry`, threads it into request processing, and calls `ReleaseAll()` during `StopAsync` when the peer tears down.

## 3. Dispatcher routing

The cleanest contract change is **router-peels-token, dispatcher unchanged for the singleton case, dispatcher gets a new optional method for instance routing**. `IServiceDispatcher` grows one method:

```csharp
public interface IServiceDispatcher
{
    string ServiceName { get; }
    Task<byte[]> DispatchAsync(string method, byte[] payload, ISerializer serializer, CancellationToken ct = default);

    // NEW: only meaningful for SubService dispatchers. Default implementation throws.
    Task<byte[]> DispatchOnInstanceAsync(
        string instanceId, string method, byte[] payload, ISerializer serializer,
        IInstanceRegistry registry, CancellationToken ct = default)
        => throw new ShaRpcNotFoundException(
            $"Service '{ServiceName}' does not support instance-scoped dispatch.");
}
```

In the per-peer inbound dispatch path (`RpcPeerInboundDispatcher` / its response builder), after looking up the dispatcher:

- If `request.InstanceId == null`: existing path, calls `DispatchAsync` (no behavioral change).
- If `request.InstanceId != null`: calls `dispatcher.DispatchOnInstanceAsync(request.InstanceId, ...)`, passing the per-connection `IInstanceRegistry`.

A SubService dispatcher's generated `DispatchOnInstanceAsync` resolves `registry.TryGet(ServiceName, instanceId, out var inst)`, casts `inst` to the interface, and runs the same switch as `DispatchAsync` but against `inst` instead of `_service`. A *root* service dispatcher only emits `DispatchAsync` (and inherits the throwing default).

When a root-service method's return type is itself a `[ShaRpcService]`, the generated case in `DispatchAsync` registers the returned object and serializes a `ServiceHandle`:

```csharp
var result = await _service.GetSubServiceAsync(arg, ct);
var id = registry.Register("ISubService", result);
return serializer.Serialize(new ServiceHandle("ISubService", id));
```

This means `DispatchAsync` also needs `IInstanceRegistry` — extend the existing signature to take it. Existing dispatchers that don't use it simply ignore the parameter.

## 4. Generator changes

**Detecting sub-service returns at semantic-analysis time.** In `ClassifyReturnType`, after unwrapping `Task<T>` / `ValueTask<T>`, check the unwrapped symbol for the presence of an attribute whose `AttributeClass.ToDisplayString() == "ShaRPC.Core.Attributes.ShaRpcServiceAttribute"`. If matched, return a new variant:

```csharp
internal enum MethodReturnKind { Void, Sync, Task, TaskOf, ValueTask, ValueTaskOf,
    TaskOfSubService, ValueTaskOfSubService }   // NEW
```

This stays incremental — it operates only on `IMethodSymbol.ReturnType` and an attribute name string, never on the full `Compilation`. `MethodModel` gets one additional value-equatable field carrying the sub-service interface's qualified name and its RPC service name (extracted from the same `[ShaRpcService(Name=...)]` attribute), both as strings, preserving record equality.

**Proxy for a sub-service-returning method.** Instead of `_invoker.InvokeAsync<ISubService>(...)` (which would fail to deserialize), emit:

```csharp
var handle = await _invoker.InvokeAsync<global::ShaRPC.Core.Protocol.ServiceHandle>(
    "IRootService", "GetSubServiceAsync", id, ct);
return new global::App.SubServiceProxy(_invoker, handle.InstanceId);
```

**Generated `SubServiceProxy` shape.** Same as today's top-level proxy with two differences:
- Constructor signature is `(IRpcInvoker invoker, string instanceId)`; stores both.
- Every `InvokeAsync(...)` call site is rewritten to `InvokeOnInstanceAsync("ISubService", _instanceId, "MethodName", ...)`.

A `[ShaRpcService]` interface is *always* emitted as a top-level proxy (callable from the root) PLUS the same class doubles as a SubServiceProxy via a second public constructor `(IRpcInvoker, string instanceId)`. The proxy carries a `_instanceId` field (nullable). When null, it emits singleton calls; when non-null, instance calls. This avoids generating two near-duplicate classes per interface.

**Out of scope (flag with SHARPC004 / SHARPC005 diagnostics, do not emit):**
- `Task<ICollection<ISubService>>`, arrays, dictionaries containing sub-services.
- `[ShaRpcService]` interfaces used as **parameter** types (server-to-client callbacks, bidirectional handles).
- `ref`/`out`/`in` sub-service parameters (already covered by SHARPC002).

## 5. Sample

User code (no change required beyond the new return type):

```csharp
[ShaRpcService] public interface IRootService { Task<ISubService> GetSubServiceAsync(string id); }
[ShaRpcService] public interface ISubService  { Task<int> CountAsync(); }
```

Generated `RootServiceProxy.GetSubServiceAsync`:

```csharp
public async global::System.Threading.Tasks.Task<global::App.ISubService> GetSubServiceAsync(string id, global::System.Threading.CancellationToken ct = default)
{
    var handle = await _invoker.InvokeAsync<string, global::ShaRPC.Core.Protocol.ServiceHandle>(
        "IRootService", "GetSubServiceAsync", id, ct);
    return new global::App.SubServiceProxy(_invoker, handle.InstanceId);
}
```

Generated `SubServiceProxy.CountAsync`:

```csharp
public async global::System.Threading.Tasks.Task<int> CountAsync(global::System.Threading.CancellationToken ct = default)
{
    if (_instanceId is null)
        return await _invoker.InvokeAsync<int>("ISubService", "CountAsync", ct);
    return await _invoker.InvokeOnInstanceAsync<int>("ISubService", _instanceId, "CountAsync", ct);
}
```

Generated `RootServiceDispatcher` case:

```csharp
case "GetSubServiceAsync":
{
    var arg = serializer.Deserialize<string>(payload);
    var result = await _service.GetSubServiceAsync(arg, ct);
    var __id = registry.Register("ISubService", result);
    return serializer.Serialize(new global::ShaRPC.Core.Protocol.ServiceHandle("ISubService", __id));
}
```

Generated `SubServiceDispatcher.DispatchOnInstanceAsync`:

```csharp
public async Task<byte[]> DispatchOnInstanceAsync(string instanceId, string method, byte[] payload, ISerializer serializer, IInstanceRegistry registry, CancellationToken ct)
{
    if (!registry.TryGet("ISubService", instanceId, out var obj) || obj is not global::App.ISubService inst)
        throw new ShaRpcNotFoundException($"Instance '{instanceId}' not found for service 'ISubService'.");
    switch (method) { case "CountAsync": return serializer.Serialize(await inst.CountAsync(ct)); /* ... */ }
}
```

New protocol type:

```csharp
namespace ShaRPC.Core.Protocol;
public sealed record ServiceHandle(string ServiceName, string InstanceId);
```

## 6. Incrementality

The detection works entirely from `IMethodSymbol.ReturnType` and a fixed attribute metadata-name string lookup — the existing `ForAttributeWithMetadataName` pipeline is unchanged. We never call `Compilation.GetSymbolsWithName` or enumerate the assembly. The new `MethodModel` fields are all `string` / enum (value-equatable). The new `SubServiceInfo` record (qualified interface name + RPC service name) is also `string`/`string` and equatable. No `Compilation`-typed value is captured in the model pipeline, so cache hit-rate stays identical to today.

The one subtlety: detecting `[ShaRpcService]` on a return type symbol requires walking `INamedTypeSymbol.GetAttributes()` for that symbol. This is a symbol query, not a compilation query, and is safe inside the existing `transform` lambda.

## 7. Explicitly NOT covered

- Collections of sub-services (`Task<IList<ISubService>>`, dictionaries, arrays). Emit SHARPC004 and refuse.
- Sub-service interfaces as **parameter** types (bidirectional / callback). Emit SHARPC005 and refuse — requires a server-initiated dispatch path that does not exist.
- Reference counting or distributed GC of instances. Lifetime is "until connection closes" plus the optional explicit-release message.
- Cross-connection instance sharing. An `InstanceId` from connection A is invisible to connection B by design.
- Authentication / authorization scoping on sub-service handles. Out of scope; carrier-level auth still applies to the connection.
- Sub-services returning sub-services arbitrarily deep — supported in principle by recursion of the rules above; no special test plan, just recursion.
- Nested generic sub-services (`ISubService<T>`). Already rejected by existing SHARPC003 (generic service interfaces).
- Serialization of a `ServiceHandle` returned in a *user DTO field* (e.g. `record Result(int Count, ISubService Inner)`). Not supported — handles must be the direct return value.

---

## Implementation checklist (in order)

1. Add `ServiceHandle` record to `src/ShaRPC.Core/Protocol/ServiceHandle.cs`.
2. Add nullable `InstanceId` property to `RpcRequest`.
3. Add new `IInstanceRegistry` interface and a default `InstanceRegistry` (per-connection, `ConcurrentDictionary<(string,string),object>`) under `src/ShaRPC.Core/Server/`.
4. Extend `IServiceDispatcher.DispatchAsync` signature with `IInstanceRegistry registry` parameter and add default `DispatchOnInstanceAsync` that throws.
5. Have `RpcPeerInboundDispatcher` own one `IInstanceRegistry` per peer and call `ReleaseAll()` during `StopAsync` on teardown.
6. Update the per-peer inbound dispatch path to branch on `request.InstanceId` and call the right dispatcher entrypoint.
7. Add `InvokeOnInstanceAsync` overloads to `IRpcInvoker` and implement in `RpcPeer` (forward to the outbound request path with `InstanceId` set).
8. Add new `MethodReturnKind` variants `TaskOfSubService` / `ValueTaskOfSubService` and a `SubServiceInfo(string InterfaceQualifiedName, string ServiceName)` value-equatable record.
9. Extend `MethodModel` with an optional `SubServiceInfo? SubService` field.
10. Update `ShaRpcGenerator.ClassifyReturnType` to detect `[ShaRpcService]` on the unwrapped return symbol and emit the new return kinds plus `SubServiceInfo`.
11. Update `ProxyGenerator` to add the second constructor `(IRpcInvoker, string instanceId)`, `_instanceId` field, and per-call branching between `InvokeAsync` and `InvokeOnInstanceAsync`.
12. Update `ProxyGenerator` to emit `ServiceHandle`-aware code for sub-service-returning methods (deserialize handle, construct sub-proxy).
13. Update `DispatcherGenerator` to emit `registry.Register(...)` + `ServiceHandle` serialization for sub-service-returning method cases.
14. Update `DispatcherGenerator` to emit a `DispatchOnInstanceAsync` override that pulls the instance from the registry and runs the same switch.
15. Add SHARPC005 diagnostic for collections-of-sub-services and SHARPC006 for sub-service-typed parameters; wire into the existing diagnostics pipeline.
16. Add Verify snapshot tests for: root + sub round-trip proxy, sub-only proxy, root dispatcher with instance registration, sub dispatcher with `DispatchOnInstanceAsync`.
17. Add an integration test in `tests/ShaRPC.Tests` exercising end-to-end root→sub→method-call with the in-process TCP transport.
18. Update `samples/Shared`, `samples/Server`, `samples/Client` with a small sub-service example demonstrating the new pattern.
19. Update `docs/api-reference.md` and `docs/quick-start.md` with a "Nested Services" section.
