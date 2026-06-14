# Generated Service Registry

ShaRPC emits a generated service registry for every compilation that contains valid
`[ShaRpcService]` interfaces. This lets callers create typed proxies and dispatchers
without scanning assemblies for generated types.

## What Gets Generated

For a shared contract assembly like this:

```csharp
using ShaRPC.Core.Attributes;

[ShaRpcService]
public interface IChatService
{
    Task SendAsync(string message, CancellationToken ct = default);
}
```

the generator emits:

- `ChatServiceProxy` in the service namespace
- `ChatServiceDispatcher` in the service namespace
- peer extension methods such as `ProvideChatService(...)` and `GetChatService()`
- `ShaRPC.Generated.ShaRpcGenerated`, a public factory and registration type
- `ShaRpcGenerated.Services`, an array-backed catalog of generated service descriptors
- `ShaRpcGenerated.RegisterServices(...)`, a generic registration callback for generated proxy implementations
- `ShaRpcGenerated.RegisterGeneratedServices(...)`, a generic callback for service/proxy/dispatcher triples

The generated `ShaRpcGenerated` type registers the service with
`ShaRPC.Core.Generated.ShaRpcServiceRegistry` through generated delegates. No runtime
type scan is needed.

Each `ShaRpcGeneratedService` descriptor contains:

- `ServiceType` - the `[ShaRpcService]` interface type
- `ProxyType` - the generated client proxy implementation type
- `DispatcherType` - the generated server dispatcher implementation type
- `ServiceName` - the wire service name after `[ShaRpcService(Name = ...)]`

## Typed Factory Usage

Use `ShaRPC.Generated.ShaRpcGenerated` when you want a generic API that does not depend
on the generated proxy or dispatcher type names:

```csharp
using ShaRPC.Core;
using ShaRPC.Core.Server;
using ShaRPC.Generated;

RpcPeer peer = /* connected peer */;
IChatService proxy = ShaRpcGenerated.CreateProxy<IChatService>(peer);

var implementation = new ChatService();
IServiceDispatcher dispatcher =
    ShaRpcGenerated.CreateDispatcher<IChatService>(implementation);
peer.Provide(dispatcher);
```

`CreateProxy<TService>` takes an `IRpcInvoker`; an `RpcPeer` implements it, so you pass
the peer directly. This is the preferred shape for frameworks, plugin hosts, and sidecars
that expose `Provide<TService>(...)` or `Remote<TService>()` style APIs.

## Generated Service Catalog

Use `ShaRpcGenerated.Services` when you need the list of generated services without
scanning the assembly for generated proxy or dispatcher types:

```csharp
using ShaRPC.Generated;

var services = ShaRpcGenerated.Services;
for (var i = 0; i < services.Count; i++)
{
    var service = services[i];
    Console.WriteLine(
        $"{service.ServiceType.FullName} -> {service.ProxyType.FullName}, {service.DispatcherType.FullName}");
}
```

`Services` is backed by one generated static array per service assembly. Accessing it
does not allocate another buffer and does not enumerate assembly types.

## Registration Sink

Use `IShaRpcServiceRegistrationSink` when a framework needs compile-time generic
registrations instead of `Type` descriptors:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ShaRPC.Core.Generated;
using ShaRPC.Generated;

public sealed class MySink : IShaRpcServiceRegistrationSink
{
    private readonly IServiceCollection _services;

    public MySink(IServiceCollection services)
    {
        _services = services;
    }

    public void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService
    {
        _services.AddTransient<TService, TImplementation>();
    }
}

ShaRpcGenerated.RegisterServices(new MySink(services));
```

For each valid `[ShaRpcService]` interface generated into the assembly,
`RegisterServices` calls:

```csharp
sink.AddService<IChatService, ChatServiceProxy>();
```

`TService` is the service interface. `TImplementation` is the generated proxy type
that implements that interface. The method is generated as direct generic calls, so it
does not scan assembly types. The generated type initializer still publishes the shared
descriptor catalog once per assembly.

Use `IShaRpcGeneratedServiceRegistrationSink` when the host needs both generated
implementation types:

```csharp
using ShaRPC.Core.Generated;
using ShaRPC.Core.Server;
using ShaRPC.Generated;

public sealed class GeneratedSink : IShaRpcGeneratedServiceRegistrationSink
{
    public void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher
    {
        // Register TService -> TProxy for clients and TDispatcher for server factories.
    }
}

ShaRpcGenerated.RegisterGeneratedServices(new GeneratedSink());
```

For the same `IChatService`, the generated method emits a direct generic call:

```csharp
sink.AddService<IChatService, ChatServiceProxy, ChatServiceDispatcher>();
```

The all-caps compatibility aliases `IShaRPCServiceRegistrationSink` and
`IShaRPCGeneratedServiceRegistrationSink` are also available for callers that prefer
the project acronym casing.

## Dynamic Factory Usage

When the service type is known only at runtime, use the non-generic overloads:

```csharp
using ShaRPC.Core;
using ShaRPC.Core.Server;
using ShaRPC.Generated;

Type serviceType = typeof(IChatService);
RpcPeer peer = /* connected peer */;
object proxy = ShaRpcGenerated.CreateProxy(serviceType, peer);

object implementation = new ChatService();
IServiceDispatcher dispatcher =
    ShaRpcGenerated.CreateDispatcher(serviceType, implementation);
peer.Provide(dispatcher);
```

The implementation passed to `CreateDispatcher(Type, object)` must implement the
service interface, otherwise the registry throws an `ArgumentException`.

When infrastructure only has an `Assembly`, use the runtime registry's targeted
lookup helper. It looks up the known generated factory type by name and returns the
same catalog that the generated static constructor published:

```csharp
using ShaRPC.Core.Generated;

IReadOnlyList<ShaRpcGeneratedService> services =
    ShaRpcServiceRegistry.GetServices(contractAssembly);
```

This is useful for plugin hosts that load contract assemblies dynamically and want
the service/proxy/dispatcher map without scanning all types in the assembly.

For hosts that load several contract assemblies, pass the assembly set once:

```csharp
Assembly[] contractAssemblies = pluginContracts.Select(p => p.Assembly).ToArray();

IReadOnlyList<ShaRpcGeneratedService> allServices =
    ShaRpcServiceRegistry.GetServices(contractAssemblies);

ShaRpcServiceRegistry.RegisterServices(contractAssemblies, new MySink(services));
ShaRpcServiceRegistry.RegisterGeneratedServices(contractAssemblies, new GeneratedSink());
```

The multi-assembly helpers perform a targeted lookup for
`ShaRPC.Generated.ShaRpcGenerated` in each assembly. They do not enumerate assembly
types or scan for attributes at runtime.

## Runtime Registry

The lower-level runtime registry is public for advanced hosts:

```csharp
using ShaRPC.Core.Generated;

var service = ShaRpcServiceRegistry.GetService<IChatService>();
var proxy = ShaRpcServiceRegistry.CreateProxy<IChatService>(peer);
var dispatcher = ShaRpcServiceRegistry.CreateDispatcher<IChatService>(implementation);
```

Like the typed factory, `CreateProxy<IChatService>` takes an `IRpcInvoker`, so pass the
connected `RpcPeer`.

Normally you should call `ShaRPC.Generated.ShaRpcGenerated` from the service assembly.
The runtime registry is useful when infrastructure code should not reference the
generated namespace directly.

## Assembly Scope

The registry is generated per compilation. If a solution has multiple shared contract
assemblies, each assembly gets its own `ShaRPC.Generated.ShaRpcGenerated` type that
registers the services declared in that assembly.

When a registry lookup is requested and the service has not been registered yet,
`ShaRpcServiceRegistry` performs one targeted lookup for the generated registration type
in the service interface's assembly and runs its static constructor. It does not enumerate
all types in the assembly.

If the source generator did not run, the registry throws a diagnostic exception that
names the service interface and assembly and tells the caller to mark the interface with
`[ShaRpcService]` and ensure the ShaRPC generator is referenced.

## Bidirectional Peer Example

The generated registry is what allows `RpcPeer` to expose a compact typed API. Each side
is an `RpcPeer` over one duplex `IRpcChannel`; each side may `Provide` an implementation
and `Get` a proxy to call the other side:

```csharp
using ShaRPC.Core;
using ShaRPC.Generated;

await using var peer = RpcPeer
    .Over(channel, serializer)
    .ProvideChatService(new ChatService())
    .Start();

IClientCallbacks callbacks = peer.GetClientCallbacks();
```

The generated `ProvideChatService` / `GetClientCallbacks` extension methods build on the
factory and registry above: `ProvideChatService(impl)` calls `peer.Provide(...)` with the
generated dispatcher, and `GetClientCallbacks()` returns the generated proxy over the peer.
If you only have `Type` values at runtime, call
`ShaRpcGenerated.CreateProxy(serviceType, peer)` and `peer.Provide(ShaRpcGenerated.CreateDispatcher(serviceType, impl))`
instead. Both sides can use the same pattern over one duplex connection.
