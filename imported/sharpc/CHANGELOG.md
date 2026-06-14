# Changelog

## Unreleased

- **BREAKING:** Removed the legacy `ShaRpcClient`, `ShaRpcServer` (and their builders /
  `IShaRpcClient` / `IShaRpcServer`), `ShaRpcPeer`, and `DuplexConnectionSplitter`. `RpcPeer`
  and `RpcHost` are now the only surface. The wire format is unchanged, so peers remain
  interoperable across versions — only the .NET API changed. Migrate
  `client.CreateXProxy()` → `peer.GetX()`, `serverBuilder.AddX(impl)` →
  `host.ForEachPeer(p => p.ProvideX(impl))`, and `ShaRpcPeer` → `RpcPeer`.
- **BREAKING:** The generated `Create…Proxy(IShaRpcClient)` and `Add…(ShaRpcServerBuilder)`
  extension methods were removed; the generator now emits only `Provide…(RpcPeer)` and
  `Get…(RpcPeer)`. The generated `ShaRpcGenerated.CreateProxy` factory now takes
  `IRpcInvoker` instead of `IShaRpcClient`.
- **BREAKING:** Removed the `IConnection` interface; `IRpcChannel` is now the sole transport unit.
  `IConnection` was a member-less alias of `IRpcChannel`, so migrating is a rename:
  `ITransport.Connection` and `IServerTransport.AcceptAsync` now return `IRpcChannel`, and custom
  transports implement `IRpcChannel` directly (the method bodies are unchanged).
- Added `RpcPeerOptions.MaxConcurrentInboundDispatch` (default 1) for bounded-concurrent
  inbound dispatch per connection: the default dispatches serially, and raising it admits up
  to that many concurrent dispatches while total in-flight inbound work stays bounded by
  `InboundQueueCapacity` + this value.
- Added `RpcPeerOptions.MaxInboundBytes` (default 64 MiB; `null` disables) to bound the total
  bytes of in-flight inbound request frames per peer. `InboundQueueCapacity` bounds frame *count*
  only, which alone permits up to `capacity × max-frame-size` bytes; this caps peak memory
  independent of frame size. A frame larger than the budget is still admitted when nothing else
  is in flight, so a single large request never deadlocks.
- Added a frame-read idle timeout to the TCP transport (`TcpConnection`, default 30s;
  `Timeout.InfiniteTimeSpan` disables), configurable via `TcpServerTransport.FrameReadIdleTimeout`
  and `TcpTransport.FrameReadIdleTimeout`. It tears down a connection whose in-progress frame read
  stalls (a slow-loris peer that declares a large frame then trickles or sends nothing), while
  leaving legitimately idle connections — those awaiting the next frame — untouched.
- **Fixed:** disposing an idle `RpcPeer`/`RpcHost` could deadlock on netstandard2.1 runtimes
  (.NET Framework, Unity/Mono) where an in-progress socket read ignores the cancellation token.
  `DisposeAsync` now closes the channel before awaiting the read loop.
- **Fixed:** `TcpConnection` now uses `ConfigureAwait(false)` on all I/O (removing a sync-context
  deadlock risk on GUI/Unity hosts), caches `RemoteEndpoint` so reading it after dispose no longer
  throws, and an outbound send racing dispose now surfaces `ShaRpcConnectionException` rather than
  hanging or leaking `ObjectDisposedException`.
- **Fixed:** a malformed request envelope with a null service name is now answered with
  `ServiceNotFound` instead of being mis-reported as an internal error; `InstanceRegistry(int)`
  validates its bound.
- Peer wait-mode inbound queues now bound retained request frames instead of staging
  excess requests in an unbounded intake queue.
- TCP tests and callers can bind `TcpServerTransport` to port `0` and read the assigned
  port from `LocalEndpoint` after start.
- `RpcPeerOptions.InboundQueueCapacity` docs now call out that `null` means an unbounded
  queue and should be reserved for trusted or externally bounded peers.
- Server-side exceptions that are not `ShaRpcException` now return a sanitized
  `Internal error.` / `ShaRpcInternalError` error payload instead of exposing the raw
  exception message and CLR exception type to remote callers.
