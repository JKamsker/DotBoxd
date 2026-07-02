---
title: 'DotBoxD Quick Start Guide'
---
> **New to DotBoxD?** Start with [Getting started](/getting-started/) and the [Tutorials](/tutorials/). This page is a deeper reference for the RPC channel layer (transports, codecs, generated registry).

Get up and running with DotBoxD in 5 minutes.

Reach for RPC channels when you want request/response host capabilities behind a shared contract:
one C# interface compiles to a typed proxy plus dispatcher, so you get easy interop with no
hand-written marshaling and no runtime reflection on the hot path (AOT-friendly, runs on
Unity/IL2CPP) — the interface is the single source of truth. Prefer a
[query/event pipeline](/tutorials/event-pipeline-runlocal/) instead when the host should receive
only server-side filtered and projected data over a one-way push, or
[pushdown](/concepts/pushdown/) when you need to collapse N round-trips into one server-side batch.

## 1. Define Your Service Contract

Create a shared library with your service interface:

```csharp
// Shared/IMyService.cs
using DotBoxD.Services.Attributes;
using MessagePack;

[DotBoxDService]
public interface IMyService
{
    Task<GreetingResponse> GreetAsync(GreetingRequest request, CancellationToken ct = default);
}

[MessagePackObject]
public class GreetingRequest
{
    [Key(0)] public string Name { get; set; } = "";
}

[MessagePackObject]
public class GreetingResponse
{
    [Key(0)] public string Message { get; set; } = "";
    [Key(1)] public DateTime ServerTime { get; set; }
}
```

## 2. Implement the Server

```csharp
// Server/MyService.cs
public class MyService : IMyService
{
    public Task<GreetingResponse> GreetAsync(GreetingRequest request, CancellationToken ct)
    {
        return Task.FromResult(new GreetingResponse
        {
            Message = $"Hello, {request.Name}!",
            ServerTime = DateTime.UtcNow
        });
    }
}

// Server/Program.cs
using DotBoxD.Services;
using DotBoxD.Services.Generated;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Transports.Tcp;

// A host turns every accepted connection into a peer.
// Each peer provides your service; the generated ProvideMyService extension wires it up.
await using var host = RpcHost
    .Listen(new TcpServerTransport(5050), new MessagePackRpcSerializer())
    .ForEachPeer(peer => peer.ProvideMyService(new MyService()));

await host.StartAsync();
Console.WriteLine("Server running on port 5050");
Console.ReadLine();

await host.StopAsync(); // DisposeAsync also stops the host
```

The port-only `TcpServerTransport` constructor binds to loopback. To accept remote TCP clients,
bind an explicit interface such as `new TcpServerTransport(IPAddress.Any, 5050)` and add your
own authentication or network access control before exposing services.

## 3. Create the Client

```csharp
// Client/Program.cs
using DotBoxD.Services;
using DotBoxD.Services.Generated;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Transports.Tcp;

var transport = new TcpTransport("localhost", 5050);
await transport.ConnectAsync();

// Over a connection, an RpcPeer can both provide and get services.
// RejectInboundCalls signals a get-only intent (this side never serves calls).
await using var peer = RpcPeer
    .Over(transport.Connection!, new MessagePackRpcSerializer(),
          new RpcPeerOptions { RejectInboundCalls = true })
    .Start();

var service = peer.GetMyService();
var response = await service.GreetAsync(new GreetingRequest { Name = "World" });

Console.WriteLine(response.Message);  // "Hello, World!"
Console.WriteLine(response.ServerTime);
```

## 4. Run It

```bash
# Terminal 1: Start server
dotnet run --project Server

# Terminal 2: Run client
dotnet run --project Client
```

## Project References

Your shared project needs these references:

> These `ProjectReference`s assume you are building inside the cloned DotBoxD repo; if you installed from NuGet ([getting started](/getting-started/)), reference the packages instead — the `DotBoxD.Services` package bundles `DotBoxD.Services.SourceGenerator` as an analyzer automatically, so you never add the generator as a standalone reference.

```xml
<ItemGroup>
  <PackageReference Include="MessagePack" Version="2.5.187" />
  <ProjectReference Include="../DotBoxD.Services/DotBoxD.Services.csproj" />
  <ProjectReference Include="../DotBoxD.Services.SourceGenerator/DotBoxD.Services.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Server and client projects reference:
- Your shared project
- `DotBoxD.Transports.Tcp`
- `DotBoxD.Codecs.MessagePack`

For process-local IPC, use the dedicated named-pipe package instead of TCP:

```sh
dotnet add package DotBoxD.Transports.NamedPipes --prerelease
```

```csharp
using DotBoxD.Transports.NamedPipes;

var serverTransport = new NamedPipeServerTransport("my-app-rpc");
var clientTransport = new NamedPipeClientTransport("my-app-rpc");
```

## What Gets Generated?

The source generator creates:

1. **Proxy** (`MyServiceProxy`) - Caller-side stub that serializes calls
2. **Dispatcher** (`MyServiceDispatcher`) - Provider-side router that deserializes and invokes
3. **Extensions** (`peer.GetMyService()`, `peer.ProvideMyService(impl)`) - Convenience methods on `RpcPeer`
4. **Registry factory** (`DotBoxDGenerated`) - Typed proxy/dispatcher factory backed by generated delegates
5. **Service catalog** (`DotBoxDGenerated.Services`) - Array-backed `GeneratedService` descriptors
6. **Registration sink** (`DotBoxDGenerated.RegisterServices(...)`) - Direct generic calls for service/proxy registrations
7. **Generated implementation sink** (`DotBoxDGenerated.RegisterGeneratedServices(...)`) - Direct generic calls for service/proxy/dispatcher registrations

You can use the generated factory directly when building framework-style APIs:

```csharp
using DotBoxD.Services.Generated;

// CreateProxy takes an IRpcInvoker — pass an RpcPeer.
var proxy = DotBoxDGenerated.CreateProxy<IMyService>(peer);
var dispatcher = DotBoxDGenerated.CreateDispatcher<IMyService>(new MyService());

foreach (var service in DotBoxDGenerated.Services)
{
    Console.WriteLine($"{service.ServiceType.Name}: {service.ProxyType.Name}");
}
```

For DI containers or host registries that need generic service/implementation pairs,
implement `IDotBoxDServiceRegistrationSink` and pass it to the generated callback:

```csharp
using DotBoxD.Services.Generated;

public sealed class MySink : IDotBoxDServiceRegistrationSink
{
    public void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService
    {
        // Register TService -> TImplementation in the host container.
    }
}

DotBoxDGenerated.RegisterServices(new MySink());
```

If the host needs both generated implementation types, use
`IDotBoxDGeneratedServiceRegistrationSink`:

```csharp
using DotBoxD.Services.Generated;
using DotBoxD.Services.Server;

public sealed class GeneratedSink : IDotBoxDGeneratedServiceRegistrationSink
{
    public void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher
    {
        // Register TService -> TProxy and TDispatcher without scanning assemblies.
    }
}

DotBoxDGenerated.RegisterGeneratedServices(new GeneratedSink());
```

## Next Steps

- [Unity Integration Guide](/channels/unity-integration/) - Full Unity setup
- [WebSocket Transport Guide](/channels/websocket-setup/) - WebSocket setup for browsers and WebGL
- [Generated Service Registry](https://github.com/JKamsker/DotBoxD/blob/main/docs/channels/generated-service-registry.md) - Factory and registry usage
- [Named Pipe Transport](/channels/named-pipe-transport/) - Local IPC setup
- [Samples](/examples/) - Working examples
- [API Reference](/channels/api-reference/) - Detailed API docs
