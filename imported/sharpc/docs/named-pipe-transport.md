# Named Pipe Transport

`ShaRPC.Transports.NamedPipes` provides first-class named-pipe client and server
transports for local process-boundary IPC. It is a separate package so hosts that only
need TCP or custom transports do not take a named-pipe dependency.

## Install

```sh
dotnet add package ShaRPC.Transports.NamedPipes
```

The package depends on `ShaRPC` and reuses `StreamConnection` for framing. That means
named-pipe traffic uses the same length validation, serialized sends, pooled receive
buffers, and clean EOF behavior as every other stream-backed ShaRPC connection.

## Host And Caller

A `RpcHost` turns every accepted named-pipe connection into an `RpcPeer`; a caller wraps a
connected pipe in its own `RpcPeer`. The generated `Provide.../Get...` extension methods replace
the old builder `AddX`/`CreateXProxy` calls.

```csharp
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.NamedPipes;

var pipeName = "my-plugin-host";

await using var host = RpcHost
    .Listen(new NamedPipeServerTransport(pipeName), new MessagePackRpcSerializer())
    .ForEachPeer(peer => peer.ProvidePluginHost(new PluginHost()));
await host.StartAsync();

await using var clientTransport = new NamedPipeClientTransport(pipeName);
await clientTransport.ConnectAsync();
await using var peer = RpcPeer
    .Over(clientTransport.Connection!, new MessagePackRpcSerializer(),
          new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5), RejectInboundCalls = true })
    .Start();

var pluginHost = peer.GetPluginHost();
await pluginHost.PingAsync();
```

`RejectInboundCalls = true` signals a get-only intent — the caller does not provide any service of
its own. Drop it (and add `Provide...` calls) when the caller also needs to be called back over the
same pipe. The host can react to lifecycle events via `host.PeerConnected`, `host.PeerDisconnected`,
and `host.AcceptError`; stop it with `await host.StopAsync()` (disposal also stops it).

Use the `(serverName, pipeName)` client constructor when connecting to a remote Windows
machine:

```csharp
var transport = new NamedPipeClientTransport("build-agent-01", "my-plugin-host");
```

## Duplex Peers

Named-pipe streams are duplex, so a single `RpcPeer` over one pipe connection can both serve
and call services when both processes need to talk over the same authenticated pipe. The
transports already hand back an `IRpcChannel` (`clientTransport.Connection!` /
`serverConnection` from `AcceptAsync`); wrap a raw `PipeStream` in `StreamConnection` only when
you manage the stream yourself. Each side calls `Provide...` for what it serves and `Get...` for
what it calls:

```csharp
using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;

Stream pipeStream = /* connected PipeStream */;
await using IRpcChannel connection = new StreamConnection(pipeStream, "pipe://plugin-host");

await using var peer = RpcPeer
    .Over(connection, new MessagePackRpcSerializer(),
          new RpcPeerOptions
          {
              RequestTimeout = TimeSpan.FromSeconds(5),
              InboundQueueCapacity = 256,
              QueueFullMode = ShaRpcQueueFullMode.Wait,
          })
    .ProvidePluginHost(new PluginHost())
    .Start();

var plugin = peer.GetPluginCallbacks();
```

For full symmetry both processes do the same thing — each wraps its end of the pipe in an
`RpcPeer`, provides its own service, and gets a proxy to the other side over the one connection.
Set `InboundQueueCapacity` (or `null` for unbounded) and `QueueFullMode` to bound how queued
inbound requests are handled under pressure, and raise `MaxConcurrentInboundDispatch` above the
default `1` for bounded-concurrent dispatch instead of strict serial-per-connection handling.

`RpcPeer.Disconnected` reports the endpoint and the closing exception, and `RpcPeer.ReadError`
surfaces read-loop failures with endpoint and error details. On a host, subscribe per peer inside
`ForEachPeer` (for example `peer.ReadError += ...`) or watch `host.PeerDisconnected` for the
aggregate signal.
