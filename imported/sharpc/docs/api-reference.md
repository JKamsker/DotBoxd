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

### Caller (single connection)

A caller is an `RpcPeerSession` over a connected transport, or an `RpcPeer` over an
already-connected channel. Use the session helper when the peer should own and dispose the
transport.

```csharp
using ShaRPC.Core;
using ShaRPC.Generated;            // generated Provide.../Get... extensions
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

var transport = new TcpTransport("127.0.0.1", 5000);
await using var session = await transport.ConnectPeerAsync(
    new MessagePackRpcSerializer(),
    new RpcPeerOptions { RejectInboundCalls = true });   // get-only intent

var svc = session.Peer.GetMyService();
var result = await svc.DoAsync(/* ... */);
```

Setting `RpcPeerOptions.RejectInboundCalls = true` makes the caller's get-only intent explicit:
the other side receives an explicit "this peer does not accept inbound calls" error rather than a
"service not found" error. It is not an authentication or authorization boundary.

#### `RpcPeer`
Symmetric endpoint over one duplex `IRpcChannel`. It can provide local services and create
generated proxies for services provided by the remote side. See the [Peer](#peer) section below
for the full member list.

| Factory / member | Description |
|------------------|-------------|
| `RpcPeer.Over(IRpcChannel, ISerializer, RpcPeerOptions?)` | Creates a peer over the channel |
| `Provide<TService>(TService)` / `Provide(IServiceDispatcher)` | Registers an inbound service (before `Start`) |
| `Get<TService>()` | Creates a generated proxy for a remote service |
| `Start()` | Begins the read loop (idempotent; invoking a method also starts it) |
| `IsConnected` | Whether the underlying channel is still connected |
| `CloseAsync()` / `DisposeAsync()` | Idempotently disposes the peer and underlying channel |

#### `RpcPeerSession`
Owns a connected `ITransport` and the `RpcPeer` running over it. This is the preferred caller
shape when integrating host-side IPC packages because it avoids transport-specific wrapper types.

| Factory / member | Description |
|------------------|-------------|
| `transport.ConnectPeerAsync(ISerializer, RpcPeerOptions?, CancellationToken)` | Connects a client transport, creates and starts a peer, and returns an owning session |
| `transport.ConnectPeerAsync(ISerializer, Action<RpcPeer>, RpcPeerOptions?, CancellationToken)` | Same as above, but configures local provided services before the read loop starts |
| `RpcPeerSession.ConnectAsync(...)` | Static equivalent for callers that prefer factory syntax |
| `Peer` | The connected peer; use generated `Get...` / `Provide...` extension methods here |
| `Get<TService>()` | Convenience proxy factory forwarding to `Peer.Get<TService>()` |
| `DisposeAsync()` | Disposes the peer first, then the transport |

---

### Host (accepting many connections)

#### `RpcHost`
Accepts connections from a listener and turns each one into an `RpcPeer`. Because each accepted
connection is a full peer, the host can both provide services to and call back into the peers
that connect to it.

```csharp
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

await using var host = RpcHost
    .Listen(new TcpServerTransport(5000), new MessagePackRpcSerializer())
    .ForEachPeer(peer => peer.ProvideMyService(new MyService()));

host.PeerConnected += (_, args) => Console.WriteLine($"connected: {args.Peer.RemoteEndpoint}");

await host.StartAsync();
// ...
await host.StopAsync();   // DisposeAsync also stops the host and closes every accepted peer
```

| Member | Description |
|--------|-------------|
| `RpcHost.Listen(IServerTransport, ISerializer, RpcPeerOptions?)` | Creates a host bound to a listener |
| `ForEachPeer(Action<RpcPeer>)` | Configures every accepted peer before its read loop starts (call `Provide.../Get...` here); can be chained |
| `StartAsync(CancellationToken)` | Starts the accept loop |
| `StopAsync(CancellationToken)` | Stops accepting, closes the listener, and closes every accepted peer |
| `DisposeAsync()` | Stops the host (if running) and disposes the listener |
| `PeerConnected` | Raised after a connection is accepted and configured (`RpcPeerEventArgs.Peer`) |
| `PeerDisconnected` | Raised when an accepted peer's read loop ends (`RpcPeerEventArgs.Peer`) |
| `AcceptError` | Raised when the accept loop catches a non-cancellation exception (`RpcHostErrorEventArgs`) |

Services provided through `ForEachPeer` are callable by any accepted peer. ShaRPC does not add
authentication or authorization; enforce access control at the transport or application layer.

#### `IServiceDispatcher`
Interface for service dispatchers (generated).

```csharp
public interface IServiceDispatcher
{
    string ServiceName { get; }
    Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default);
}
```

---

### Peer

#### `RpcPeer`
Symmetric endpoint over one duplex `IRpcChannel`. It can provide local services and create
generated proxies for services provided by the remote side.

| Member | Description |
|--------|-------------|
| `RpcPeer.Over(IRpcChannel, ISerializer, RpcPeerOptions?)` | Creates a peer over the channel; call `Start()` (or invoke a method) to begin the read loop |
| `Provide<TService>(TService)` / `Provide(IServiceDispatcher)` | Registers an inbound service (must be called before the peer starts) |
| `Get<TService>()` | Creates a generated proxy for a remote service |
| `Start()` | Begins the read loop; idempotent and chainable |
| `IsConnected` / `RemoteEndpoint` | Channel connection state and remote endpoint string |
| `Disconnected` | Raised when a remote close or read error ends the read loop; local close/dispose does not raise it. Handlers run on the teardown path and should not block |
| `ReadError` | Raised when the read loop faults |
| `ProtocolError` | Raised when a malformed or unsupported protocol frame is observed |
| `DispatchError` | Raised when inbound request dispatch or response sending fails after a request was accepted |
| `CloseAsync()` / `DisposeAsync()` | Idempotently disposes the peer and underlying connection; closed peers cannot be restarted |

#### Bidirectional usage

Both sides of a connection are `RpcPeer` instances over one duplex `IRpcChannel`. Each side may
`Provide` services and `Get` proxies, so calls flow in both directions over the same connection.

```csharp
// Generated Provide.../Get... extension method names drop the leading "I" of the interface
// (IChatRoom -> ProvideChatRoom / GetChatRoom).

// Side A provides a chat room and can call back into B.
await using var a = RpcPeer
    .Over(channelA, serializer)
    .ProvideChatRoom(new ChatRoom())
    .Start();
var participant = a.GetChatParticipant();   // A calls the connecting peer

// Side B provides the participant callback and calls the room.
await using var b = RpcPeer
    .Over(channelB, serializer)
    .ProvideChatParticipant(new ChatParticipant())
    .Start();
var room = b.GetChatRoom();                 // B calls A
```

On a host, the per-connection peer is configured in `RpcHost.ForEachPeer`; obtain the peer from
`PeerConnected` (`args.Peer`) to call back into a connecting peer over the same connection.

Cancelling an in-flight outbound call sends a ShaRPC cancel frame for that request. The receiving
peer continues reading the connection while dispatch runs and cancels the matching dispatcher
token when that frame arrives.

#### `RpcPeerOptions`
Options for both `RpcPeer` and `RpcHost`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RequestTimeout` | `TimeSpan` | 30s | Per-call timeout for proxies. Use `Timeout.InfiniteTimeSpan` to disable |
| `EnableLowAllocationValueTaskInvocations` | `bool` | `false` | Opts generated generic `ValueTask<T>` unary proxy calls into the pooled response-source path. This alone does not guarantee that path: the call must use the non-timeout/non-cancellable call shape, and the transport/runtime must support the low-allocation path; otherwise the proxy uses the `Task<T>`-backed path |
| `ServiceProvider` | `IServiceProvider?` | `null` | Resolves dependencies for dispatcher factories and `Provide<TService>()` |
| `RejectInboundCalls` | `bool` | `false` | Answers inbound requests with an explicit "does not accept inbound calls" error; makes get-only intent explicit. Not an auth boundary |
| `DisableInboundRequestCancellation` | `bool` | `false` | Disables per-request cancellation state for non-streaming inbound calls. Handlers receive `CancellationToken.None`; inbound Cancel frames for those calls are ignored |
| `InboundQueueCapacity` | `int?` | 1024 | Max queued inbound requests (bounded read-side backpressure). `null` dispatches immediately and does not cap concurrent dispatch work — trusted/bounded transports only |
| `MaxConcurrentInboundDispatch` | `int` | 1 | Max inbound requests dispatched concurrently when `InboundQueueCapacity` is set. Default `1` dispatches serially per connection; raise it for bounded-concurrent per-connection dispatch. Ignored when `InboundQueueCapacity` is `null` |
| `MaxInboundBytes` | `long?` | 64 MiB | Max total bytes of in-flight inbound request frames when `InboundQueueCapacity` is set. Caps peak memory independent of frame count; `null` disables. An oversized frame is still admitted when nothing else is in flight, so one large request never deadlocks. Ignored when `InboundQueueCapacity` is `null` |
| `MaxPendingRequests` | `int` | 4096 | Max concurrent outbound calls awaiting responses |
| `QueueFullMode` | `ShaRpcQueueFullMode` | `Wait` | Policy when `InboundQueueCapacity` is set and the request queue is full (`Wait` applies backpressure; `DropIncoming` rejects) |

The TCP transport additionally enforces a per-frame read idle timeout (`TcpConnection`, default 30s;
`Timeout.InfiniteTimeSpan` disables), set via `TcpServerTransport.FrameReadIdleTimeout` /
`TcpTransport.FrameReadIdleTimeout`. It tears down a connection whose *in-progress* frame read stalls
(a slow-loris peer that declares a large frame then trickles or sends nothing), but never times out a
connection idly awaiting its next request.

`RejectInboundCalls` is not an authentication or authorization boundary. Any connected peer can
still send request frames; secure transports or application-level checks should enforce trust.

Setting `InboundQueueCapacity` to `null` dispatches inbound peer requests immediately and does not
cap concurrent dispatcher work; use that only with trusted peers or externally bounded transports.
In `Wait` mode, queued requests are bounded and read-side backpressure applies instead of retaining
unbounded request frames.

The default profile is intentionally safe: outbound calls have timeouts, inbound handlers receive
cancellable tokens, inbound dispatch is bounded, and generated `ValueTask<T>` proxies use the
`Task<T>`-backed path. For measured hot paths that can trade those guarantees for lower allocation,
see [Performance Hot Paths](./performance.md).

---

### Transport

#### `ITransport`
Client-side transport interface.

```csharp
public interface ITransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    IRpcChannel? Connection { get; }
    bool IsConnected { get; }
}
```

#### `IServerTransport`
Server-side transport interface.

```csharp
public interface IServerTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);
    Task<IRpcChannel> AcceptAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
```

#### `IRpcChannel`
The duplex, framed channel an `RpcPeer` runs on. Responses flow back over the same channel, so it
is always bidirectional even when the call direction is one-way.

```csharp
public interface IRpcChannel : IAsyncDisposable
{
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
    Task<Payload> ReceiveAsync(CancellationToken ct = default);
    bool IsConnected { get; }
    string RemoteEndpoint { get; }
}
```

A `Payload` with `Length` of 0 returned from `ReceiveAsync` signals the channel was closed. The
caller owns the returned `Payload` and must dispose it. Implement `IRpcChannel` to add a custom
transport.

#### Built-in single-connection primitives

| Type | Description |
|------|-------------|
| `StreamConnection` | `IRpcChannel` over any duplex `Stream`, including `PipeStream`; reads and writes complete ShaRPC length-prefixed frames |
| `SingleConnectionTransport` | Client `ITransport` adapter for an already-established `IRpcChannel` |
| `SingleConnectionServerTransport` | Server `IServerTransport` adapter that accepts one already-established `IRpcChannel` |
| `RpcPeerSession` | Transport-owned client peer session returned by `ConnectPeerAsync` |

---

## Named Pipe Transport: `ShaRPC.Transports.NamedPipes`

#### `NamedPipeClientTransport`
Named-pipe client transport for process-boundary IPC.

```csharp
public NamedPipeClientTransport(string pipeName, int maxMessageSize = MessageFramer.MaxMessageSize)
public NamedPipeClientTransport(string serverName, string pipeName, int maxMessageSize = MessageFramer.MaxMessageSize)
```

#### `NamedPipeServerTransport`
Named-pipe server transport.

```csharp
public NamedPipeServerTransport(
    string pipeName,
    int maxAllowedServerInstances = NamedPipeServerStream.MaxAllowedServerInstances,
    int maxMessageSize = MessageFramer.MaxMessageSize)
```

Both transports wrap `NamedPipeClientStream`/`NamedPipeServerStream` in the core
`StreamConnection`, so they use the same ShaRPC frame validation, send serialization,
and clean EOF behavior as any other stream-backed connection.

```csharp
// Host side
await using var host = RpcHost
    .Listen(new NamedPipeServerTransport("my-plugin-pipe"), new MessagePackRpcSerializer())
    .ForEachPeer(peer => peer.ProvideMyService(new MyService()));
await host.StartAsync();

// Caller side
var transport = new NamedPipeClientTransport("my-plugin-pipe");
await using var session = await transport.ConnectPeerAsync(new MessagePackRpcSerializer());
var svc = session.Peer.GetMyService();
```

---

### Serialization

#### `ISerializer`
Serialization interface.

```csharp
public interface ISerializer
{
    void Serialize<T>(IBufferWriter<byte> writer, T value);
    T Deserialize<T>(ReadOnlyMemory<byte> data);
    object? Deserialize(ReadOnlyMemory<byte> data, Type type);
}
```

---

### Exceptions

| Exception | Description |
|-----------|-------------|
| `ShaRpcException` | Base exception for all ShaRPC errors |
| `ShaRpcRemoteException` | Remote error (includes `RemoteExceptionType`); non-`ShaRpcException` server failures are sanitized |
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

`TcpServerTransport.LocalEndpoint` exposes the bound endpoint after `StartAsync` succeeds,
including the OS-assigned port when the transport is created with port `0`.

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

// Custom resolver with ShaRPC binary payload formatters and standard fallbacks
var serializer = MessagePackRpcSerializer.CreateWithResolver(myResolver);
```

| Method | Description |
|--------|-------------|
| `CreateUnityCompatible()` | Creates serializer optimized for Unity/AOT |
| `CreateWithResolver(IFormatterResolver)` | Creates serializer with a custom resolver chain |
| `CreateOptions(params IFormatterResolver[])` | Builds hardened MessagePack options with ShaRPC formatters |

The default options include a formatter for `ReadOnlyMemory<byte>` so binary DTO fields encode as MessagePack bin payloads.

---

## Generated Extensions

For each `[ShaRpcService]` interface `IFooService`, the generator creates `RpcPeer` extension
methods. The method suffix drops the leading `I` of the interface name
(`IFooService` -> `ProvideFooService` / `GetFooService`):

```csharp
// In namespace ShaRPC.Generated
public static class ShaRpcGeneratedExtensions
{
    // Provide a local implementation for the other peer to call (before the peer starts).
    public static RpcPeer ProvideFooService(this RpcPeer peer, IFooService implementation);

    // Get a proxy to call IFooService on the other peer.
    public static IFooService GetFooService(this RpcPeer peer);
}
```

The generator also emits a public factory class and registers factories with the runtime registry.
The proxy factories take an `IRpcInvoker` (an `RpcPeer` implements it), so pass the peer directly:

```csharp
// In namespace ShaRPC.Generated
public static class ShaRpcGenerated
{
    public static IReadOnlyList<ShaRpcGeneratedService> Services { get; }
    public static void RegisterServices(IShaRpcServiceRegistrationSink sink);
    public static void RegisterGeneratedServices(IShaRpcGeneratedServiceRegistrationSink sink);
    public static TService CreateProxy<TService>(IRpcInvoker invoker) where TService : class;
    public static object CreateProxy(Type serviceInterface, IRpcInvoker invoker);
    public static IServiceDispatcher CreateDispatcher<TService>(TService implementation) where TService : class;
    public static IServiceDispatcher CreateDispatcher(Type serviceInterface, object implementation);
}
```

`CreateDispatcher<TService>(impl)` produces an `IServiceDispatcher` you register with
`peer.Provide(dispatcher)`; `CreateProxy<TService>(invoker)` produces a proxy bound to the peer
(equivalent to `peer.Get<TService>()` and the generated `Get...` extension).

`ShaRpcGenerated.Services` is backed by a generated static array of `ShaRpcGeneratedService`
records. Each descriptor includes `ServiceType`, `ProxyType`, `DispatcherType`, and
`ServiceName`, so hosts can build a service map without scanning assembly types.

`RegisterServices(IShaRpcServiceRegistrationSink)` emits one direct generic call per
service, using the generated proxy as `TImplementation`:

```csharp
public interface IShaRpcServiceRegistrationSink
{
    void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService;
}
```

`RegisterGeneratedServices(IShaRpcGeneratedServiceRegistrationSink)` emits one direct
generic call per service with service, proxy, and dispatcher types:

```csharp
public interface IShaRpcGeneratedServiceRegistrationSink
{
    void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher;
}
```

The runtime registry is available as `ShaRPC.Core.Generated.ShaRpcServiceRegistry` and throws a clear diagnostic when no generated factory is registered for a service interface.
It also exposes `GetService(Type)`, `GetServices(Assembly)`, `GetServices(IEnumerable<Assembly>)`,
and multi-assembly sink registration helpers for dynamic hosts that need generated metadata.
See [Generated Service Registry](./generated-service-registry.md) for examples and assembly-scope details.

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
| Message Type | 1 byte | 0x01=Request, 0x02=Response, 0x03=Error, 0x04=Cancel |
| Envelope Length | 4 bytes (int32 LE) | Size of the serialized envelope |
| Envelope | Variable | Serialized `RpcRequest`/`RpcResponse` metadata |
| Payload | Variable | Raw serialized arguments/return value |

> [!NOTE]
> All multi-byte integers are in LE (Little Endian) format.

The payload is **not** nested inside the envelope. It is appended as raw trailing bytes so the
receiver can hand it to the dispatcher (or deserialize the return value) as a zero-copy slice of the
frame buffer, avoiding a per-message heap allocation. The envelope-length prefix lets the receiver
locate the payload without the serializer reporting how many bytes it consumed.

Cancel frames use only the 9-byte frame header and the message id of the request being
cancelled; they do not include an RPC envelope.

### Message Types

| Value | Type | Description |
|-------|------|-------------|
| `0x01` | Request | RPC request from client |
| `0x02` | Response | Successful response from server |
| `0x03` | Error | Error response from server |
| `0x04` | Cancel | Envelope-less cancellation frame for an in-flight request id |

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
