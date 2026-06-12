# Feature Request: Make the ShaRPC IPC Addon Transport-Agnostic

## Summary

`SafeIR.Transport.Ipc.ShaRpc` should keep the convenient named-pipe helpers, but its core API should accept ShaRPC's generic transport abstractions instead of storing concrete named-pipe transport types.

This keeps Safe-IR's addon boundary intact while letting hosts use named pipes, TCP, in-memory test transports, or future custom ShaRPC transports without changing the addon.

## Current Friction

The current addon shape is effectively named-pipe-specific:

```csharp
SafeIrShaRpcMessagePackIpc.ListenNamedPipe(...);
SafeIrShaRpcMessagePackIpc.ConnectNamedPipeAsync(...);
```

The client handle also owns a concrete `NamedPipeClientTransport`. That makes the addon harder to reuse when the host wants another ShaRPC transport, even though ShaRPC itself is already built around:

- `IRpcChannel`
- `ITransport`
- `IServerTransport`
- `RpcPeer`
- `RpcHost`

## Recommendation

Add transport-agnostic overloads as the addon core:

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

Then keep named-pipe methods as small convenience wrappers:

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
    => ConnectAsync(new NamedPipeClientTransport(pipeName), options, cancellationToken);
```

ShaRPC now exposes a transport-owned peer session helper, so Safe-IR should not need its own named-pipe-specific disposable wrapper. The host-side usage can become:

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

This should stay in `SafeIR.Transport.Ipc.ShaRpc`. Do not move ShaRPC, MessagePack, streams, sockets, or transport objects into Safe-IR core/runtime/hosting.

The sandbox network facade and the plugin/control-plane IPC transport are separate concerns:

- Sandbox network APIs must stay capability-checked, audited, pinned, budgeted, and disabled by default.
- ShaRPC IPC is a host-owned control-plane transport between trusted host/plugin processes.

Opening raw networking or arbitrary transport objects to sandboxed code would weaken Safe-IR's threat model. The addon should help host code connect peers; it should not become a sandbox-visible network capability.

## Acceptance Criteria

- `SafeIR.Transport.Ipc.ShaRpc` exposes generic `ITransport` and `IServerTransport` entry points.
- Existing named-pipe APIs remain as convenience wrappers.
- The client handle no longer stores `NamedPipeClientTransport`; it either uses ShaRPC's `RpcPeerSession` or stores `ITransport`.
- Safe-IR core/runtime/hosting continue to have no ShaRPC or MessagePack references.
- IPC examples still use named pipes by default.
- A test or sample demonstrates using a non-named-pipe ShaRPC transport through the same addon API.

## Package Note

The branch currently references ShaRPC `1.0.0-ci.18`. Upgrade to a package containing the new `RpcPeerSession` / `ConnectPeerAsync` API before adopting this shape.
