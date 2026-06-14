# Generated Service Registry

DotBoxd emits a generated service registry for every compilation that contains valid
`[DotBoxdService]` interfaces. This lets callers create typed proxies and dispatchers
without scanning assemblies for generated types.

## What Gets Generated

For a shared contract assembly like this:

```csharp
using DotBoxd.Services.Attributes;

[DotBoxdService]
public interface IChatService
{
    Task SendAsync(string message, CancellationToken ct = default);
}
```

the generator emits:

- `ChatServiceProxy` in the service namespace
- `ChatServiceDispatcher` in the service namespace
- peer extension methods such as `ProvideChatService(...)` and `GetChatService()`
- `DotBoxd.Services.Generated.DotBoxdGenerated`, a public factory and registration type
- `DotBoxdGenerated.Services`, an array-backed catalog of generated service descriptors
- `DotBoxdGenerated.RegisterServices(...)`, a generic registration callback for generated proxy implementations
- `DotBoxdGenerated.RegisterGeneratedServices(...)`, a generic callback for service/proxy/dispatcher triples

The generated `DotBoxdGenerated` type registers the service with
`DotBoxd.Services.Generated.DotBoxdServiceRegistry` through generated delegates. No runtime
type scan is needed.

Each `DotBoxdGeneratedService` descriptor contains:

- `ServiceType` - the `[DotBoxdService]` interface type
- `ProxyType` - the generated client proxy implementation type
- `DispatcherType` - the generated server dispatcher implementation type
- `ServiceName` - the wire service name after `[DotBoxdService(Name = ...)]`

## Typed Factory Usage

Use `DotBoxd.Services.Generated.DotBoxdGenerated` when you want a generic API that does not depend
on the generated proxy or dispatcher type names:

```csharp
using DotBoxd.Services;
using DotBoxd.Services.Server;
using DotBoxd.Services.Generated;

RpcPeer peer = /* connected peer */;
IChatService proxy = DotBoxdGenerated.CreateProxy<IChatService>(peer);

var implementation = new ChatService();
IServiceDispatcher dispatcher =
    DotBoxdGenerated.CreateDispatcher<IChatService>(implementation);
peer.Provide(dispatcher);
```

`CreateProxy<TService>` takes an `IRpcInvoker`; an `RpcPeer` implements it, so you pass
the peer directly. This is the preferred shape for frameworks, plugin hosts, and sidecars
that expose `Provide<TService>(...)` or `Remote<TService>()` style APIs.

## Generated Service Catalog

Use `DotBoxdGenerated.Services` when you need the list of generated services without
scanning the assembly for generated proxy or dispatcher types:

```csharp
using DotBoxd.Services.Generated;

var services = DotBoxdGenerated.Services;
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

Use `IDotBoxdServiceRegistrationSink` when a framework needs compile-time generic
registrations instead of `Type` descriptors:

```csharp
using Microsoft.Extensions.DependencyInjection;
using DotBoxd.Services.Generated;
using DotBoxd.Services.Generated;

public sealed class MySink : IDotBoxdServiceRegistrationSink
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

DotBoxdGenerated.RegisterServices(new MySink(services));
```

For each valid `[DotBoxdService]` interface generated into the assembly,
`RegisterServices` calls:

```csharp
sink.AddService<IChatService, ChatServiceProxy>();
```

`TService` is the service interface. `TImplementation` is the generated proxy type
that implements that interface. The method is generated as direct generic calls, so it
does not scan assembly types. The generated type initializer still publishes the shared
descriptor catalog once per assembly.

Use `IDotBoxdGeneratedServiceRegistrationSink` when the host needs both generated
implementation types:

```csharp
using DotBoxd.Services.Generated;
using DotBoxd.Services.Server;
using DotBoxd.Services.Generated;

public sealed class GeneratedSink : IDotBoxdGeneratedServiceRegistrationSink
{
    public void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher
    {
        // Register TService -> TProxy for clients and TDispatcher for server factories.
    }
}

DotBoxdGenerated.RegisterGeneratedServices(new GeneratedSink());
```

For the same `IChatService`, the generated method emits a direct generic call:

```csharp
sink.AddService<IChatService, ChatServiceProxy, ChatServiceDispatcher>();
```

The all-caps compatibility aliases `IDotBoxdServiceRegistrationSink` and
`IDotBoxdGeneratedServiceRegistrationSink` are also available for callers that prefer
the project acronym casing.

## Dynamic Factory Usage

When the service type is known only at runtime, use the non-generic overloads:

```csharp
using DotBoxd.Services;
using DotBoxd.Services.Server;
using DotBoxd.Services.Generated;

Type serviceType = typeof(IChatService);
RpcPeer peer = /* connected peer */;
object proxy = DotBoxdGenerated.CreateProxy(serviceType, peer);

object implementation = new ChatService();
IServiceDispatcher dispatcher =
    DotBoxdGenerated.CreateDispatcher(serviceType, implementation);
peer.Provide(dispatcher);
```

The implementation passed to `CreateDispatcher(Type, object)` must implement the
service interface, otherwise the registry throws an `ArgumentException`.

When infrastructure only has an `Assembly`, use the runtime registry's targeted
lookup helper. It looks up the known generated factory type by name and returns the
same catalog that the generated static constructor published:

```csharp
using DotBoxd.Services.Generated;

IReadOnlyList<DotBoxdGeneratedService> services =
    DotBoxdServiceRegistry.GetServices(contractAssembly);
```

This is useful for plugin hosts that load contract assemblies dynamically and want
the service/proxy/dispatcher map without scanning all types in the assembly.

For hosts that load several contract assemblies, pass the assembly set once:

```csharp
Assembly[] contractAssemblies = pluginContracts.Select(p => p.Assembly).ToArray();

IReadOnlyList<DotBoxdGeneratedService> allServices =
    DotBoxdServiceRegistry.GetServices(contractAssemblies);

DotBoxdServiceRegistry.RegisterServices(contractAssemblies, new MySink(services));
DotBoxdServiceRegistry.RegisterGeneratedServices(contractAssemblies, new GeneratedSink());
```

The multi-assembly helpers perform a targeted lookup for
`DotBoxd.Services.Generated.DotBoxdGenerated` in each assembly. They do not enumerate assembly
types or scan for attributes at runtime.

## Runtime Registry

The lower-level runtime registry is public for advanced hosts:

```csharp
using DotBoxd.Services.Generated;

var service = DotBoxdServiceRegistry.GetService<IChatService>();
var proxy = DotBoxdServiceRegistry.CreateProxy<IChatService>(peer);
var dispatcher = DotBoxdServiceRegistry.CreateDispatcher<IChatService>(implementation);
```

Like the typed factory, `CreateProxy<IChatService>` takes an `IRpcInvoker`, so pass the
connected `RpcPeer`.

Normally you should call `DotBoxd.Services.Generated.DotBoxdGenerated` from the service assembly.
The runtime registry is useful when infrastructure code should not reference the
generated namespace directly.

## Assembly Scope

The registry is generated per compilation. If a solution has multiple shared contract
assemblies, each assembly gets its own `DotBoxd.Services.Generated.DotBoxdGenerated` type that
registers the services declared in that assembly.

When a registry lookup is requested and the service has not been registered yet,
`DotBoxdServiceRegistry` performs one targeted lookup for the generated registration type
in the service interface's assembly and runs its static constructor. It does not enumerate
all types in the assembly.

If the source generator did not run, the registry throws a diagnostic exception that
names the service interface and assembly and tells the caller to mark the interface with
`[DotBoxdService]` and ensure the DotBoxd generator is referenced.

## Bidirectional Peer Example

The generated registry is what allows `RpcPeer` to expose a compact typed API. Each side
is an `RpcPeer` over one duplex `IRpcChannel`; each side may `Provide` an implementation
and `Get` a proxy to call the other side:

```csharp
using DotBoxd.Services;
using DotBoxd.Services.Generated;

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
`DotBoxdGenerated.CreateProxy(serviceType, peer)` and `peer.Provide(DotBoxdGenerated.CreateDispatcher(serviceType, impl))`
instead. Both sides can use the same pattern over one duplex connection.
