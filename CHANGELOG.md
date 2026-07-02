# Changelog

## [Unreleased] — DotBoxD

This release establishes **DotBoxD**, a single contract-first .NET extension runtime spanning
Services, Kernels, and Pushdown.

- **Merged repositories:** the formerly standalone ShaRPC (RPC framework) and Safe-IR (kernel
  sandbox) projects were combined into one monorepo via history-preserving subtree merges. See
  [Migration from standalone repos](https://dotboxd.kamsker.at/contributing/migration-from-standalone-repos/)
  for how to view pre-merge history.
- **Full rebrand:** all assemblies, namespaces, attributes, and diagnostics renamed to the
  `DotBoxD.*` family (`[RpcService]`, `DBXS###` services diagnostics, `DBXK###` kernel/plugin
  diagnostics). The wire format is unchanged.
- **Central Package Management (CPM):** versions are managed centrally via
  `Directory.Packages.props`; the solution uses the `DotBoxD.slnx` format.
- **New CI / release pipelines:** cross-platform build/test (net8/9/10 on Windows and Ubuntu),
  security & quality gates (rebrand completeness, file-length, spec manifest, API baseline,
  security-boundary tests, docs smoke, GameServer example smoke), CodeQL, benchmarks, and a tag-driven
  release pipeline with provenance attestation.
- **Meta-packages:** `DotBoxD` (net10.0, full stack) and `DotBoxD.Services.All` (netstandard2.1,
  Unity/IL2CPP service bundle).
- **GameServer example:** `samples/GameServer/Examples.GameServer.Server` is the maintained
  runnable example for service IPC, event kernels, live settings, host bindings, policies, and
  kernel RPC. Removed sample coverage is tracked in
  [the examples coverage-gaps page](https://dotboxd.kamsker.at/examples/coverage-gaps/).
- **Server-extension DTO parameters & broader type support:** `[ServerExtensionMethod]` entrypoints now
  accept record/value-object parameters — including nested DTOs and plain `class` DTOs — on the grafted
  client path, matching the typed-proxy path (issue #41). The proxy and grafted clients now share one
  marshaller, which also fixes invalid generated C# for a record return whose field is a `List<T>`.
  Additionally: **enums** marshal through their underlying integer (params, returns, and DTO fields); DTOs
  with `init`/`set` properties and no matching constructor are reconstructed via an object initializer
  (parity with the runtime marshaller); and a DTO that **inherits public properties** from a base type is
  now rejected with a clear diagnostic instead of silently dropping the inherited fields.
  `samples/GameServer` adds a `WorldRangeQuery` value-object example
  (`RangeMonsterKillerKernel.KillMonstersInRangeAsync`).
- **Server-extension `Dictionary<K,V>` / map support:** `[ServerExtensionMethod]` entrypoints now accept
  `Dictionary<K,V>` / `IReadOnlyDictionary<K,V>` / `IDictionary<K,V>` as parameters, returns, and nested DTO
  fields (issue #44). This adds a wire `Map` kind to `KernelRpcValue` (entries are a flat key/value
  sequence; additive public-API change) with codec, converter, and runtime-marshaller support, and the
  generated client marshals dictionaries to/from it. A kernel body can also read a map (`dict[key]`,
  `dict.ContainsKey(key)`) and build one (`new Dictionary<K,V>()`, `dict[key] = value`), lowered to the
  `map.get`/`map.containsKey`/`map.empty`/`map.set` kernel intrinsics. Map keys must be scalar
  (`bool`/`int`/`long`/`string`/enum); a non-scalar key is rejected at generation time. Not yet supported
  (no runtime intrinsic): `dict.Count`, iterating a map, and `dict.Add`/`dict.Remove`.
- **Documentation & repo polish:** new top-level README, `docs/` information architecture
  (getting-started, concepts, security, reference, contributing), `SECURITY.md`, `CONTRIBUTING.md`,
  `CODE_OF_CONDUCT.md`, and GitHub repo metadata files.
- **BREAKING — public de-brand:** over-branded public types had the redundant `DotBoxD` prefix
  stripped now that their namespace already conveys the brand. The wire format is unchanged; only
  the .NET type names changed. Renames:
  `DotBoxDDotBoxDRpcMessagePackIpc` → `RpcMessagePackIpc` (also fixes a double-brand bug);
  service exceptions in `DotBoxD.Services.Exceptions`:
  `DotBoxDRpcException` → `ServiceException`,
  `DotBoxDRpcNotFoundException` → `ServiceNotFoundException`,
  `DotBoxDRpcTimeoutException` → `ServiceTimeoutException`,
  `DotBoxDRpcProtocolException` → `ServiceProtocolException`,
  `DotBoxDRpcConnectionException` → `ServiceConnectionException`,
  `DotBoxDRpcRemoteException` → `RemoteServiceException`;
  `DotBoxDRpcQueueFullMode` → `QueueFullMode`;
  `DotBoxDJsonImporter` → `JsonImporter`, `DotBoxDJsonExporter` → `JsonExporter`,
  `DotBoxDJsonSchemas` → `JsonSchemas`;
  `DotBoxDServiceRegistry` → `GeneratedServiceRegistry`,
  `DotBoxDGeneratedService` → `GeneratedService`, `DotBoxDGeneratedMethod` → `GeneratedMethod`,
  `DotBoxDGeneratedParameter` → `GeneratedParameter`, `DotBoxDGeneratedReturnKind` → `GeneratedReturnKind`;
  `DotBoxDNamedPipeOptions` → `NamedPipeTransportOptions`;
  `DotBoxDPluginAnalyzer` → `PluginAnalyzer`, `DotBoxDPluginPackageGenerator` → `PluginPackageGenerator`.
  Sanctioned public brand entry points are unchanged: `[RpcService]` / `RpcServiceAttribute`,
  `[RpcMethod]` / `RpcMethodAttribute`, `DotBoxDGenerated`, `DotBoxDGeneratedExtensions`,
  `DotBoxDInfo`, `DotBoxDServicesInfo`, and all `DotBoxD.*` assembly/namespace names.

---

The entries below predate the DotBoxD rebrand and refer to the former ShaRPC API names. They are
retained verbatim as historical record (CHANGELOG is excluded from the rebrand-completeness gate).

## Unreleased (pre-rebrand, ShaRPC history)

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
