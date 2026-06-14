# Implemented: Make the DotBoxd IPC Addon Transport-Agnostic

Status: implemented in `DotBoxd.Pushdown.Services`.

## Summary

`DotBoxd.Pushdown.Services` should keep the convenient named-pipe helpers, but its core API should accept DotBoxd's generic transport abstractions instead of storing concrete named-pipe transport types.

This keeps Safe-IR's addon boundary intact while letting hosts use named pipes, TCP, in-memory test transports, or future custom DotBoxd transports without changing the addon.

## Original Friction

The original addon shape was effectively named-pipe-specific:

```csharp
DotBoxdDotBoxdRpcMessagePackIpc.ListenNamedPipe(...);
DotBoxdDotBoxdRpcMessagePackIpc.ConnectNamedPipeAsync(...);
```

The client handle also owns a concrete `NamedPipeClientTransport`. That makes the addon harder to reuse when the host wants another DotBoxd transport, even though DotBoxd itself is already built around:

- `IRpcChannel`
- `ITransport`
- `IServerTransport`
- `RpcPeer`
- `RpcHost`

## Implemented Shape

The addon core exposes transport-agnostic overloads:

```csharp
public static RpcHost Listen(
    IServerTransport transport,
    Action<RpcPeer> configurePeer,
    RpcPeerOptions? options = null);

public static Task<RpcPeerSession> ConnectAsync(
    ITransport transport,
    RpcPeerOptions? options = null,
    CancellationToken cancellationToken = default);
```

Named-pipe methods remain small convenience wrappers:

```csharp
public static RpcHost ListenNamedPipe(
    string pipeName,
    Action<RpcPeer> configurePeer,
    RpcPeerOptions? options = null)
    => Listen(new NamedPipeServerTransport(pipeName), configurePeer, options);

public static Task<RpcPeerSession> ConnectNamedPipeAsync(
    string pipeName,
    RpcPeerOptions? options = null,
    CancellationToken cancellationToken = default)
    => ConnectNamedPipeAsync(".", pipeName, options, cancellationToken);

public static Task<RpcPeerSession> ConnectNamedPipeAsync(
    string serverName,
    string pipeName,
    RpcPeerOptions? options = null,
    CancellationToken cancellationToken = default)
    => ConnectAsync(new NamedPipeClientTransport(serverName, pipeName), options, cancellationToken);
```

DotBoxd now exposes a transport-owned peer session helper, so Safe-IR should not need its own named-pipe-specific disposable wrapper. The host-side usage can become:

```csharp
await using var session = await transport.ConnectPeerAsync(
    serializer,
    options ?? DefaultClientOptions,
    cancellationToken);

var service = session.Get<IPluginControlService>();
```

If the client also needs to provide callback services, use the configure overload:

```csharp
await using var session = await transport.ConnectPeerAsync(
    serializer,
    peer => peer.ProvidePluginCallbacks(callbacks),
    options,
    cancellationToken);
```

## Keep This Out Of Safe-IR Core

This should stay in `DotBoxd.Pushdown.Services`. Do not move DotBoxd, MessagePack, streams, sockets, or transport objects into Safe-IR core/runtime/hosting.

The sandbox network facade and the plugin/control-plane IPC transport are separate concerns:

- Sandbox network APIs must stay capability-checked, audited, pinned, budgeted, and disabled by default.
- DotBoxd IPC is a host-owned control-plane transport between trusted host/plugin processes.

Opening raw networking or arbitrary transport objects to sandboxed code would weaken Safe-IR's threat model. The addon should help host code connect peers; it should not become a sandbox-visible network capability.

## Acceptance Criteria

- `DotBoxd.Pushdown.Services` exposes generic `ITransport` and `IServerTransport` entry points.
- Existing named-pipe APIs remain as convenience wrappers.
- The client handle no longer stores `NamedPipeClientTransport`; it either uses DotBoxd's `RpcPeerSession` or stores `ITransport`.
- Safe-IR core/runtime/hosting continue to have no DotBoxd or MessagePack references.
- IPC examples still use named pipes by default.
- A test or sample demonstrates using a non-named-pipe DotBoxd transport through the same addon API.

## Verification

- `DotBoxdRpcIpcAddonTests.Generic_transport_api_connects_over_non_named_pipe_transport`
  demonstrates the generic API over an in-memory test transport.
- `DotBoxdRpcIpcAddonTests.Configured_generic_client_can_provide_callback_services`
  covers bidirectional client peer configuration.
- `AddonBoundaryTests` keeps DotBoxd, MessagePack, and transport addon references out
  of Safe-IR core/runtime/hosting projects.
