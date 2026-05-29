# ShaRPC API Reference

## Core Namespace: `ShaRPC.Core`

### Attributes

#### `ShaRpcServiceAttribute`
Marks an interface as a ShaRPC service.

```csharp
[ShaRpcService]
public interface IMyService { }

// With custom name
[ShaRpcService(Name = "CustomServiceName")]
public interface IMyService { }
```

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string?` | Custom service name (default: interface name) |

#### `ShaRpcMethodAttribute`
Optionally customizes an RPC method.

```csharp
[ShaRpcMethod(Name = "CustomMethodName")]
Task<Result> MyMethodAsync(Request req, CancellationToken ct = default);
```

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string?` | Custom method name (default: method name) |

---

### Client

#### `IShaRpcClient`
Interface for the RPC client.

```csharp
public interface IShaRpcClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string method, TRequest request, CancellationToken ct = default);
    Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default);
    Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default);
    bool IsConnected { get; }
}
```

#### `ShaRpcClient`
Default implementation of `IShaRpcClient`.

```csharp
public ShaRpcClient(ITransport transport, ISerializer serializer, TimeSpan? timeout = null)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `transport` | `ITransport` | Transport for network communication |
| `serializer` | `ISerializer` | Serializer for message encoding |
| `timeout` | `TimeSpan?` | Request timeout (default: 30 seconds) |

#### `ShaRpcClientBuilder`
Fluent builder for creating clients.

```csharp
var client = new ShaRpcClientBuilder()
    .UseTransport(transport)
    .UseSerializer(serializer)
    .WithTimeout(TimeSpan.FromSeconds(10))
    .Build();
```

| Method | Description |
|--------|-------------|
| `UseTransport(ITransport)` | Sets the transport |
| `UseSerializer(ISerializer)` | Sets the serializer |
| `WithTimeout(TimeSpan)` | Sets default request timeout |
| `Build()` | Creates the client instance |

---

### Server

#### `IShaRpcServer`
Interface for the RPC server.

```csharp
public interface IShaRpcServer : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
```

#### `ShaRpcServer`
Default implementation of `IShaRpcServer`.

```csharp
public ShaRpcServer(IServerTransport transport, ISerializer serializer)
```

| Method | Description |
|--------|-------------|
| `RegisterDispatcher(IServiceDispatcher)` | Registers a service dispatcher |
| `StartAsync(CancellationToken)` | Starts accepting connections |
| `StopAsync(CancellationToken)` | Stops the server gracefully |

#### `ShaRpcServerBuilder`
Fluent builder for creating servers.

```csharp
var server = new ShaRpcServerBuilder()
    .UseTransport(transport)
    .UseSerializer(serializer)
    .AddDispatcher(dispatcher)
    .Build();
```

| Method | Description |
|--------|-------------|
| `UseTransport(IServerTransport)` | Sets the server transport |
| `UseSerializer(ISerializer)` | Sets the serializer |
| `AddDispatcher(IServiceDispatcher)` | Registers a dispatcher |
| `AddService<TService, TDispatcher>(TService)` | Registers with implementation |
| `Build(IServiceProvider?)` | Creates the server instance |

#### `IServiceDispatcher`
Interface for service dispatchers (generated).

```csharp
public interface IServiceDispatcher
{
    string ServiceName { get; }
    Task<byte[]> DispatchAsync(string method, byte[] payload, ISerializer serializer, CancellationToken ct = default);
}
```

---

### Transport

#### `ITransport`
Client-side transport interface.

```csharp
public interface ITransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    IConnection? Connection { get; }
    bool IsConnected { get; }
}
```

#### `IServerTransport`
Server-side transport interface.

```csharp
public interface IServerTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);
    Task<IConnection> AcceptAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
```

#### `IConnection`
Represents an active connection.

```csharp
public interface IConnection : IAsyncDisposable
{
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
    Task<Memory<byte>> ReceiveAsync(CancellationToken ct = default);
    bool IsConnected { get; }
    string RemoteEndpoint { get; }
}
```

---

### Serialization

#### `ISerializer`
Serialization interface.

```csharp
public interface ISerializer
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(ReadOnlySpan<byte> data);
    object? Deserialize(ReadOnlySpan<byte> data, Type type);
}
```

---

### Exceptions

| Exception | Description |
|-----------|-------------|
| `ShaRpcException` | Base exception for all ShaRPC errors |
| `ShaRpcRemoteException` | Server-side exception (includes `RemoteExceptionType`) |
| `ShaRpcConnectionException` | Connection lost or failed |
| `ShaRpcTimeoutException` | Request timed out |
| `ShaRpcNotFoundException` | Service or method not found |

---

## TCP Transport: `ShaRPC.Transports.Tcp`

#### `TcpTransport`
TCP client transport.

```csharp
public TcpTransport(string host, int port)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `host` | `string` | Server hostname or IP |
| `port` | `int` | Server port |

#### `TcpServerTransport`
TCP server transport.

```csharp
public TcpServerTransport(int port)
public TcpServerTransport(IPAddress address, int port)
public TcpServerTransport(string address, int port)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `port` | `int` | Port to listen on |
| `address` | `IPAddress`/`string` | Interface to bind (default: `IPAddress.Any`) |

---

## MessagePack Serializer: `ShaRPC.Serializers.MessagePack`

#### `MessagePackRpcSerializer`
MessagePack-based serializer.

```csharp
// Default configuration
var serializer = new MessagePackRpcSerializer();

// Unity-compatible (contractless)
var serializer = MessagePackRpcSerializer.CreateUnityCompatible();

// Custom options
var serializer = new MessagePackRpcSerializer(customOptions);
```

| Method | Description |
|--------|-------------|
| `CreateUnityCompatible()` | Creates serializer optimized for Unity/AOT |

---

## Generated Extensions

For each `[ShaRpcService]` interface `IFooService`, the generator creates:

```csharp
// In namespace ShaRPC.Generated
public static class ShaRpcGeneratedExtensions
{
    // Client extension
    public static IFooService CreateFooServiceProxy(this IShaRpcClient client);

    // Server builder extension
    public static ShaRpcServerBuilder AddFooService(
        this ShaRpcServerBuilder builder,
        IFooService implementation);
}
```

---

## Protocol Format

### Wire Format

```
[4 bytes: Total Length][4 bytes: MessageId][1 byte: MessageType][4 bytes: Envelope Length][E bytes: Envelope][P bytes: Payload]
```

| Field | Size | Description |
|-------|------|-------------|
| Total Length | 4 bytes (int32 LE) | Full message size including header |
| Message ID | 4 bytes (int32 LE) | Request/response correlation ID |
| Message Type | 1 byte | 0x01=Request, 0x02=Response, 0x03=Error |
| Envelope Length | 4 bytes (int32 LE) | Size of the serialized envelope |
| Envelope | Variable | Serialized `RpcRequest`/`RpcResponse` metadata |
| Payload | Variable | Raw serialized arguments/return value |

> [!NOTE]
> All multi-byte integers are in LE (Little Endian) format.

The payload is **not** nested inside the envelope. It is appended as raw trailing bytes so the
receiver can hand it to the dispatcher (or deserialize the return value) as a zero-copy slice of the
frame buffer, avoiding a per-message heap allocation. The envelope-length prefix lets the receiver
locate the payload without the serializer reporting how many bytes it consumed.

### Message Types

| Value | Type | Description |
|-------|------|-------------|
| `0x01` | Request | RPC request from client |
| `0x02` | Response | Successful response from server |
| `0x03` | Error | Error response from server |
| `0x04` | Cancel | Cancellation (reserved for future) |

### Request Envelope

```csharp
public class RpcRequest
{
    public int MessageId { get; set; }
    public string ServiceName { get; set; }
    public string MethodName { get; set; }
    public string? InstanceId { get; set; }  // Target sub-service instance, null for singletons
}
```

The serialized method arguments travel as the frame's trailing payload, not inside this envelope.

### Response Envelope

```csharp
public class RpcResponse
{
    public int MessageId { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorType { get; set; }
}
```

The serialized return value travels as the frame's trailing payload, not inside this envelope.
