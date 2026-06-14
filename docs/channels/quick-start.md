# DotBoxd Quick Start Guide

Get up and running with DotBoxd in 5 minutes.

## 1. Define Your Service Contract

Create a shared library with your service interface:

```csharp
// Shared/IMyService.cs
using DotBoxd.Services.Attributes;
using MessagePack;

[DotBoxdService]
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
using DotBoxd.Services;
using DotBoxd.Services.Generated;
using DotBoxd.Codecs.MessagePack;
using DotBoxd.Transports.Tcp;

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

## 3. Create the Client

```csharp
// Client/Program.cs
using DotBoxd.Services;
using DotBoxd.Services.Generated;
using DotBoxd.Codecs.MessagePack;
using DotBoxd.Transports.Tcp;

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

```xml
<ItemGroup>
  <PackageReference Include="MessagePack" Version="2.5.187" />
  <ProjectReference Include="../DotBoxd.Services/DotBoxd.Services.csproj" />
  <ProjectReference Include="../DotBoxd.Services.SourceGenerator/DotBoxd.Services.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Server and client projects reference:
- Your shared project
- `DotBoxd.Transports.Tcp`
- `DotBoxd.Codecs.MessagePack`

For process-local IPC, use the dedicated named-pipe package instead of TCP:

```sh
dotnet add package DotBoxd.Transports.NamedPipes
```

```csharp
using DotBoxd.Transports.NamedPipes;

var serverTransport = new NamedPipeServerTransport("my-app-rpc");
var clientTransport = new NamedPipeClientTransport("my-app-rpc");
```

## What Gets Generated?

The source generator creates:

1. **Proxy** (`MyServiceProxy`) - Caller-side stub that serializes calls
2. **Dispatcher** (`MyServiceDispatcher`) - Provider-side router that deserializes and invokes
3. **Extensions** (`peer.GetMyService()`, `peer.ProvideMyService(impl)`) - Convenience methods on `RpcPeer`
4. **Registry factory** (`DotBoxdGenerated`) - Typed proxy/dispatcher factory backed by generated delegates
5. **Service catalog** (`DotBoxdGenerated.Services`) - Array-backed `DotBoxdGeneratedService` descriptors
6. **Registration sink** (`DotBoxdGenerated.RegisterServices(...)`) - Direct generic calls for service/proxy registrations
7. **Generated implementation sink** (`DotBoxdGenerated.RegisterGeneratedServices(...)`) - Direct generic calls for service/proxy/dispatcher registrations

You can use the generated factory directly when building framework-style APIs:

```csharp
using DotBoxd.Services.Generated;

// CreateProxy takes an IRpcInvoker — pass an RpcPeer.
var proxy = DotBoxdGenerated.CreateProxy<IMyService>(peer);
var dispatcher = DotBoxdGenerated.CreateDispatcher<IMyService>(new MyService());

foreach (var service in DotBoxdGenerated.Services)
{
    Console.WriteLine($"{service.ServiceType.Name}: {service.ProxyType.Name}");
}
```

For DI containers or host registries that need generic service/implementation pairs,
implement `IDotBoxdServiceRegistrationSink` and pass it to the generated callback:

```csharp
using DotBoxd.Services.Generated;
using DotBoxd.Services.Generated;

public sealed class MySink : IDotBoxdServiceRegistrationSink
{
    public void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService
    {
        // Register TService -> TImplementation in the host container.
    }
}

DotBoxdGenerated.RegisterServices(new MySink());
```

If the host needs both generated implementation types, use
`IDotBoxdGeneratedServiceRegistrationSink`:

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
        // Register TService -> TProxy and TDispatcher without scanning assemblies.
    }
}

DotBoxdGenerated.RegisterGeneratedServices(new GeneratedSink());
```

## Next Steps

- [Unity Integration Guide](./unity-integration.md) - Full Unity setup
- [WebSocket Transport Guide](./websocket-setup.md) - WebSocket setup for browsers and WebGL
- [Generated Service Registry](./generated-service-registry.md) - Factory and registry usage
- [Named Pipe Transport](./named-pipe-transport.md) - Local IPC setup
- [Samples](../samples/) - Working examples
- [API Reference](./api-reference.md) - Detailed API docs
