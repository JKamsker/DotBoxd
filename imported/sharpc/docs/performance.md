# Performance Hot Paths

ShaRPC defaults favor safe behavior: per-call timeouts, inbound cancellation tokens, bounded
inbound queues, and `Task`-backed proxy invocation. That costs some allocation, but it is the
right default for most applications.

Use the low-allocation switches only on measured hot paths where both peers are trusted and the
work is bounded elsewhere.

## Near-zero allocation unary calls

The near-zero allocation path is for non-streaming unary calls returning `ValueTask<T>`.
It requires all of these conditions:

- The service contract returns `ValueTask<T>` for the hot method.
- The caller uses the generated proxy and awaits each returned `ValueTask<T>` exactly once.
- The hot call does not pass a cancellable `CancellationToken`.
- The caller peer has no per-call timeout and opts into pooled `ValueTask<T>` responses.
- The serving peer disables non-streaming inbound request cancellation for that trusted hot path.
- The serving peer is externally bounded, or its inbound queue is intentionally disabled.
- The transport implements `IRpcFrameChannel` so complete pooled frames can move without creating
  a received `Payload` per frame.
- The application avoids per-call DTO/result allocation where practical, for example by reusing
  immutable request objects and returning cached immutable result objects.

Caller-side options:

```csharp
var callerOptions = new RpcPeerOptions
{
    EnableLowAllocationValueTaskInvocations = true,
    RequestTimeout = Timeout.InfiniteTimeSpan,
    RejectInboundCalls = true, // Only when this peer is call-only.
};
```

Server-side options for a trusted non-streaming hot path:

```csharp
var serverOptions = new RpcPeerOptions
{
    DisableInboundRequestCancellation = true,
    InboundQueueCapacity = null,
};
```

If that server peer also makes outbound hot-path calls, apply the caller-side options to it too.

## Tradeoffs

`EnableLowAllocationValueTaskInvocations` is default-off. When enabled, generic `ValueTask<T>`
unary calls can use a pooled `IValueTaskSource<T>` instead of the default `Task<T>` path. This
can run continuations inline on the peer read loop, so continuations must stay short and should
not block. It also means normal `ValueTask<T>` rules matter: await it once, and do not call
`AsTask()` repeatedly.

`RequestTimeout = Timeout.InfiniteTimeSpan` removes ShaRPC's per-call timeout on outbound calls.
Use this only when the transport, protocol above ShaRPC, or application workflow already has a
deadline. Passing a cancellable caller token keeps safety but intentionally falls back to the
allocating `Task<T>` path.

`DisableInboundRequestCancellation = true` avoids one inbound cancellation allocation for
non-streaming calls. The handler receives `CancellationToken.None`, inbound Cancel frames for
those calls are ignored, and peer shutdown waits for the handler to finish. Leave it disabled
when service code depends on cancellation or untrusted callers can start expensive work.

`InboundQueueCapacity = null` removes the bounded dispatch queue and read-side backpressure. Use
it only for trusted peers, single-purpose in-process links, or transports that impose their own
strict bounds. The default bounded queue is safer for shared, networked, or adversarial inputs.

Near-zero allocation is a whole-path property, not a guarantee for every call. Serializers,
transports, logging, DTO creation, result creation, cancellation, timeouts, and streaming can all
allocate.
