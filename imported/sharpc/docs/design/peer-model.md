# ShaRPC Peer Model

Status: implemented on `feature/peer-model`. The legacy `ShaRpcClient` / `ShaRpcServer` /
`ShaRpcPeer` and the `DuplexConnectionSplitter` have since been **removed** — `RpcPeer` and
`RpcHost` are the only surface. The wire format is unchanged, so the removal is API-only.

This document describes the move from a `client` / `server` mental model to a single,
symmetric **peer** model: two sides connect over a transport, either side can *provide*
implementations of `[ShaRpcService]` interfaces, and either side can *call* the
implementations the other side provides. Bidirectional is the default capability;
one-directional ("I call you and get data back, you cannot call me") is just the
asymmetric configuration of the same type.

## Principles

1. **One symmetric type.** `RpcPeer` is *this side* of a connection. It can `Provide`
   interface implementations (callable by the other side) and `Get` proxies (to call the
   other side). Either, or both.
2. **Direction is configuration, not type.** A "client" is a peer that only `Get`s. A
   "server" is a peer that only `Provide`s. Bidirectional is a peer that does both. There
   is no separate client/server class on the hot path.
3. **One read loop.** The peer demuxes inbound frames by message type — `Response`/`Error`
   complete *my* pending calls; `Request`/`Cancel` hit *my* dispatchers. No connection
   splitter, no stacked server + client.
4. **Transport-agnostic, duplex channel.** The transport unit is a duplex `IRpcChannel`
   (responses must flow back). Accepting many connections is a separate `RpcHost` concern.
5. **Wire-compatible.** Frame format, envelopes (`RpcRequest`/`RpcResponse`), and
   sub-service handles are unchanged, so new peers interoperate with the legacy
   client/server and existing serializers/transports keep working.

## Why the existing protocol already supports symmetry

The four message types already encode *direction* independently of *who sent the frame*:

- A `Response`/`Error` is, by definition, "an answer to a request **I** sent" → it
  completes an entry in my pending-call table.
- A `Request`/`Cancel` is, by definition, "something the **other side** asked of me" → it
  is routed to my dispatcher.

So message-id spaces never collide across directions even when both peers number their
outbound requests from the same counter: the *type* tells you which way a frame flows. The
old `DuplexConnectionSplitter` was an elaborate way of exploiting that fact by physically
separating the two streams so a stock `ShaRpcServer` and stock `ShaRpcClient` could each
pretend they owned the socket. `RpcPeer` exploits it directly with a single loop.

## Core abstractions

### `IRpcChannel` — the transport unit

```csharp
namespace ShaRPC.Core.Transport;

// A duplex, framed, bidirectional channel. One per peer.
public interface IRpcChannel : IAsyncDisposable
{
    Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);
    Task<Payload> ReceiveAsync(CancellationToken ct = default);   // Length 0 == remote closed
    bool IsConnected { get; }
    string RemoteEndpoint { get; }
}
```

`IRpcChannel` is the sole transport unit. Every existing connection
(`StreamConnection`, `TcpConnection`, named pipes) implements it directly. (The transitional
`IConnection : IRpcChannel` alias has since been removed; transports return `IRpcChannel`.)

> The channel is always duplex because results flow back. The *call* asymmetry lives in
> the peer (provide vs. get), not in the transport. Genuinely send-only links
> (fire-and-forget / notifications) are an explicitly out-of-scope future addition.

### `IRpcInvoker` — what generated proxies depend on

```csharp
namespace ShaRPC.Core;

public interface IRpcInvoker
{
    Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string method, TRequest request, CancellationToken ct = default);
    Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default);
    Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default);
    Task InvokeAsync(string service, string method, CancellationToken ct = default);

    // Sub-service (capability handle) calls.
    Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default);
    Task<TResponse> InvokeOnInstanceAsync<TResponse>(string service, string instanceId, string method, CancellationToken ct = default);
    Task InvokeOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default);
    Task InvokeOnInstanceAsync(string service, string instanceId, string method, CancellationToken ct = default);
}
```

This is the transport-agnostic set of invoke verbs — no `Connect`/`IsConnected`/`Dispose`.
Generated proxies depend on `IRpcInvoker`, which `RpcPeer` implements directly — no "client"
required.

### `RpcPeer`

```csharp
namespace ShaRPC.Core;

public sealed class RpcPeer : IAsyncDisposable, IRpcInvoker
{
    public static RpcPeer Over(IRpcChannel channel, ISerializer serializer, RpcPeerOptions? options = null);

    // exports (what THIS side provides)
    public RpcPeer Provide<TService>(TService implementation) where TService : class;
    public RpcPeer Provide(IServiceDispatcher dispatcher);

    // imports (what THIS side calls on the other)
    public TService Get<TService>() where TService : class;

    // lifecycle
    public RpcPeer Start();                 // begins the single read loop; idempotent; returns this
    public bool IsConnected { get; }
    public string RemoteEndpoint { get; }
    public Task CloseAsync(CancellationToken ct = default);
    public ValueTask DisposeAsync();

    // observability
    public event EventHandler<RpcDisconnectedEventArgs>? Disconnected; // handlers should not block
    public event EventHandler<RpcReadErrorEventArgs>? ReadError;
    public event EventHandler<RpcProtocolErrorEventArgs>? ProtocolError;
}
```

Fluent so both ends read symmetrically:

```csharp
await using var peer = RpcPeer
    .Over(channel, serializer)
    .Provide<IChatRoom>(new ChatRoom())
    .Start();

var participant = peer.Get<IChatParticipant>();
await participant.JoinedAsync("Jonas");
```

### `RpcPeerOptions`

`RpcPeerOptions` is a sealed class with validating init-only properties (it is not a `record`:
the init accessors reject invalid values such as a non-positive `InboundQueueCapacity`,
`MaxPendingRequests`, or `RequestTimeout`, so it does not carry value-equality semantics).

```csharp
public sealed class RpcPeerOptions
{
    // Validated: must be positive (<= int.MaxValue ms) or Timeout.InfiniteTimeSpan.
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public IServiceProvider? ServiceProvider { get; init; }

    // When true, inbound Request frames are answered with a clear "this peer accepts no
    // inbound calls" error. Makes a get-only ("client") peer's one-directional intent
    // explicit instead of returning "service not found". This is an intent signal, not
    // an authentication or authorization boundary.
    public bool RejectInboundCalls { get; init; }

    // Backpressure for inbound dispatch. Defaults to a bounded queue (1024). Null dispatches
    // immediately and does not cap concurrent dispatcher work; use it only with trusted
    // peers or externally bounded transports. In Wait mode, queued requests are bounded
    // and excess request frames apply read-side backpressure until dispatch queue space is
    // available.
    //
    // Because a peer demuxes responses, cancels, and inbound requests over a single read
    // loop, a full Wait-mode queue parks that loop — which also pauses reading responses to
    // this peer's own outbound calls. A bidirectional peer whose inbound handlers call back
    // into the same peer must size this above the number of inbound requests that can arrive
    // ahead of those callbacks' responses, or use null / DropIncoming; otherwise an
    // under-sized Wait queue can stall a reentrant response until RequestTimeout.
    public int? InboundQueueCapacity { get; init; } = 1024;
    public int MaxPendingRequests { get; init; } = 4096;
    public ShaRpcQueueFullMode QueueFullMode { get; init; } = ShaRpcQueueFullMode.Wait;
}
```

### `RpcHost` — accepting many connections

The accept loop that lived inside `ShaRpcServer` moves into a host whose *output is peers*.
Because each accepted connection is a full peer, the host can also call back into
connecting peers.

```csharp
namespace ShaRPC.Core;

public sealed class RpcHost : IAsyncDisposable
{
    public static RpcHost Listen(IServerTransport listener, ISerializer serializer, RpcPeerOptions? options = null);

    // Runs for every accepted connection, before its read loop starts.
    public RpcHost ForEachPeer(Action<RpcPeer> configure);

    public Task StartAsync(CancellationToken ct = default);
    public Task StopAsync(CancellationToken ct = default);
    public ValueTask DisposeAsync();

    public event EventHandler<RpcPeerEventArgs>? PeerConnected;
    public event EventHandler<RpcPeerEventArgs>? PeerDisconnected;
    public event EventHandler<RpcHostErrorEventArgs>? AcceptError;
}
```

`IServerTransport` is kept as the listener abstraction (it yields `IRpcChannel`).
"IChannelListener" is the conceptual name for the same role.

## Generated surface (per `[ShaRpcService] IGameService`)

The generator emits the same proxy + dispatcher pair. The proxy's backing field is typed
`IRpcInvoker` and named `_invoker` internally. The generated extension methods target
`RpcPeer` (`Provide…` / `Get…`); the legacy `Create…Proxy` / `Add…` extensions were removed
along with the client/server types.

```csharp
public static class ShaRpcGeneratedExtensions
{
    public static RpcPeer ProvideGameService(this RpcPeer peer, IGameService impl);   // → Provide<IGameService>(impl)
    public static IGameService GetGameService(this RpcPeer peer);                      // → Get<IGameService>()
}
```

## Usage examples

### One-directional ("client" calls "server")

```csharp
// Provider side.
await using var host = RpcHost
    .Listen(new TcpServerTransport(5050), new MessagePackRpcSerializer())
    .ForEachPeer(peer => peer.ProvideGameService(new GameService()));
await host.StartAsync();

// Caller side — gets only; RejectInboundCalls makes the one-way intent explicit.
var channel = /* connect a duplex IRpcChannel */;
await using var peer = RpcPeer
    .Over(channel, new MessagePackRpcSerializer(), new RpcPeerOptions { RejectInboundCalls = true })
    .Start();

var game = peer.GetGameService();
var status = await game.GetServerStatusAsync();
```

### Bidirectional

```csharp
await using var a = RpcPeer.Over(channelA, serializer)
    .Provide<IChatParticipant>(new ParticipantA())
    .Start();
var room = a.Get<IChatRoom>();
await room.JoinAsync("A");

await using var b = RpcPeer.Over(channelB, serializer)
    .Provide<IChatRoom>(new ChatRoom())
    .Start();
var notifier = b.Get<IChatParticipant>();   // B calls back into A
await notifier.MessageReceivedAsync("welcome");
```

## Migration map

| Was | Now | Notes |
|---|---|---|
| `ShaRpcClient` / `IShaRpcClient` | `RpcPeer` (get-only) / `IRpcInvoker` | legacy client **removed** |
| `ShaRpcServer` + accept loop | `RpcHost` + per-conn `RpcPeer` | legacy server **removed**; accept concern is `RpcHost` |
| `ShaRpcPeer` + `DuplexConnectionSplitter` | `RpcPeer` (one loop) | both **removed** |
| `IConnection` | `IRpcChannel` | `IConnection` **removed**; transports return `IRpcChannel` directly (impls unchanged — `IConnection` added no members) |
| `serverBuilder.AddGameService(impl)` | `peer.ProvideGameService(impl)` | generated `Add…` extension **removed** |
| `client.CreateGameServiceProxy()` | `peer.GetGameService()` | generated `Create…Proxy` extension **removed** |
| `RpcRequest` / `RpcResponse` / `ServiceHandle` / `InstanceRegistry` | **unchanged** | wire compatibility preserved |

The wire format never changes, so old and new peers interoperate throughout.
