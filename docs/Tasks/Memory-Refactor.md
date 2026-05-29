# Plan: Pool buffers across ShaRPC (eliminate `new byte[]`)

## Context

ShaRPC currently allocates a fresh `byte[]` for every frame, every payload, and every
serializer call, and copies the whole received buffer twice (`new MemoryStream(data.ToArray())`
in both client and server). On a hot RPC path this is constant GC pressure. The goal is to
**rent from `ArrayPool<byte>.Shared` instead of allocating**, and to use
`Span`/`Memory`/`IMemoryOwner` for fixed-size buffers. The user specifically asked that the
payload returned from `MessageFramer.ReadMessageAsync` become a `Payload` class.

**Scope decision (confirmed with user):** pragmatic pooling now — pool every frame, send
buffer, and serializer output; eliminate both `MemoryStream(.ToArray())` copies. Accept the
**one** small array MessagePack allocates per received message to back the nested
`RpcRequest/RpcResponse.Payload` member. That nesting is what makes early buffer-return
memory-safe (see invariant below). A follow-up task to push to true zero-alloc receive is
documented under *Future Work*.

All runtime libs target **netstandard2.1** — every API below (`ArrayPool`, `IBufferWriter<byte>`,
`IMemoryOwner<byte>`, `BinaryPrimitives`, `Stream.ReadAsync(Memory)`) is available there, and
MessagePack 2.5.187 has verified `Serialize(IBufferWriter<byte>,…)` / `Deserialize<T>(ReadOnlyMemory<byte>,…)`
overloads plus built-in formatters for `byte[]`, `ReadOnlyMemory<byte>`, `Memory<byte>` (identical
wire bytes — so the DTO member type change is **not** a wire break).

### Core safety invariant (must be preserved + commented in code)
The receive loop does `serializer.Deserialize<RpcResponse>(frameSlice)`. Under
`MessagePackSecurity.UntrustedData` (already configured), MessagePack **always copies** a nested
`ReadOnlyMemory<byte>` member into a fresh heap array — it never aliases the input. Therefore the
rented frame `Payload` can be disposed immediately after deserialize, even though the awaiting
caller deserializes `response.Payload` later on another thread. **Do not** "optimize" the inner
payload into a zero-copy slice of the frame buffer without also extending the frame's lifetime —
that would reintroduce a use-after-free.

---

## New types (in `src/ShaRPC.Core`)

### `ShaRPC.Core.Buffers.Payload` (new file `Buffers/Payload.cs`)
`sealed class Payload : IMemoryOwner<byte>` — owns one rented array.
- `static Payload Empty` — singleton wrapping `Array.Empty<byte>()`; `Dispose()` is a **guaranteed no-op**.
- `static Payload Rent(int length)` — `length==0` ⇒ `Empty`; else rent `>= length`, remember exact `length`.
- `Memory<byte> Memory` ⇒ `_array.AsMemory(0,_length)` (mutable — required by `IMemoryOwner`; fill path needs it; document "treat as read-only after receipt").
- `ReadOnlySpan<byte> Span`, `int Length`.
- `void Dispose()` — idempotent: null the field, return the **original** rented array to the pool exactly once. Single-owner type.
- internal ctor `(byte[] rented, int length)` used by `Rent`, `PooledBufferWriter.DetachPayload`, `MessageFramer`.

### `ShaRPC.Core.Buffers.PooledBufferWriter` (new file `Buffers/PooledBufferWriter.cs`)
`sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable`.
- ctor `(int initialCapacity = 256)` rents backing array.
- `GetSpan/GetMemory(int sizeHint=0)` — ensure capacity (`required = _written + max(sizeHint,1)`; grow ⇒ rent `max(required, _buffer.Length*2)`, **copy exactly `_written` bytes**, return old), guarantee ≥1 byte.
- `Advance(int)` — throw if it would exceed current buffer.
- `ReadOnlyMemory<byte> WrittenMemory`, `int WrittenCount`.
- `Payload DetachPayload()` — hand the rented array + `_written` to a new `Payload`, null own reference; **throw `InvalidOperationException` if already detached/disposed**.
- `Dispose()` — return buffer to pool; **no-op after `DetachPayload`**.

### `ShaRPC.Core.Serialization.SerializerExtensions` (new file)
`static Payload SerializeToPayload<T>(this ISerializer s, T value)` ⇒ `using var w = new PooledBufferWriter(); s.Serialize(w, value); return w.DetachPayload();`

---

## Changed public APIs

| File | Change |
|------|--------|
| `src/ShaRPC.Core/Serialization/ISerializer.cs` | `void Serialize<T>(IBufferWriter<byte> writer, T value)`; `T Deserialize<T>(ReadOnlyMemory<byte> data)`; `object? Deserialize(ReadOnlyMemory<byte> data, Type type)` |
| `src/ShaRPC.Serializers.MessagePack/MessagePackRpcSerializer.cs` | implement new interface via `MessagePackSerializer.Serialize(writer,…)` / `Deserialize<T>(ReadOnlyMemory,…)` — **drop both `.ToArray()`** |
| `src/ShaRPC.Core/Protocol/RpcRequest.cs`, `RpcResponse.cs` | `byte[] Payload` → `ReadOnlyMemory<byte> Payload` (default `ReadOnlyMemory<byte>.Empty`) |
| `src/ShaRPC.Core/Transport/IConnection.cs` | `Task<Payload> ReceiveAsync(CancellationToken)` (`Payload.Empty`/`Length==0` = closed) |
| `src/ShaRPC.Core/Server/IServiceDispatcher.cs` | both `DispatchAsync`/`DispatchOnInstanceAsync` → `Task<Payload>`, payload param `ReadOnlyMemory<byte>` |

### `src/ShaRPC.Core/Protocol/MessageFramer.cs`
Keep `HeaderSize`, `MaxMessageSize`, `ReadExactAsync`. Replace `byte[] Frame(...)` and add:
- `void WriteFrame(IBufferWriter<byte> w, int id, MessageType type, ReadOnlySpan<byte> payload)` — primitive.
- `Payload FrameToPayload(int id, MessageType type, ReadOnlySpan<byte> payload)` — rents exact-size, header + payload.
- `Payload FrameMessage<T>(ISerializer s, int id, MessageType type, T body)` — **one** `PooledBufferWriter`: write 9-byte header with placeholder length (`Advance(9)`), append `s.Serialize(writer, body)`, `DetachPayload()`, then patch total length into `payload.Span[0..4]` with `BinaryPrimitives.WriteInt32LittleEndian`. (Used by client + server.)
- `bool TryReadFrame(ReadOnlyMemory<byte> src, out int id, out MessageType type, out ReadOnlyMemory<byte> payload)` — **zero-copy slice** parse (runtime path).
- `Task<FramedMessage?> ReadMessageAsync(Stream, CancellationToken)` — returns `readonly record struct FramedMessage(int MessageId, MessageType Type, Payload Payload)`; rents the payload buffer (`null` still = closed). Keep `WriteMessageAsync(Stream,…)` (writes via `WriteFrame` into a `PooledBufferWriter`). These stream methods are now test/stream-transport facing.

### `src/ShaRPC.Transports.Tcp/TcpConnection.cs`
`ReceiveAsync`: read 4-byte length (keep small rented temp), validate against `MessageFramer.MaxMessageSize` (replace hardcoded `16*1024*1024`), `Payload.Rent(totalLength)`, write the length prefix into the first 4 bytes, `ReadExactAsync` into `payload.Memory.Slice(4)`, return it. **Dispose the rented `Payload` on every short-read / cancel / error return** (leak fix), e.g. try/catch around the post-rent read.

---

## Source generator (`src/ShaRPC.SourceGenerator/DispatcherGenerator.cs`) — the big one

Proxy generator is **unchanged** (it only forwards to the generic `_client.InvokeAsync<…>` overloads — verified; proxy snapshots stay byte-identical). In `DispatcherGenerator.cs` change only the emitted strings (all `global::`-qualified):
- L85/L89 signatures: `Task<byte[]>` → `Task<global::ShaRPC.Core.Buffers.Payload>`; `byte[] payload` → `global::System.ReadOnlyMemory<byte> payload`.
- arg-deserialize lines (L145/L158) — text unchanged; now binds the `ReadOnlyMemory` overload.
- `Void`/`Task`/`ValueTask` returns (L195/L206): `global::System.Array.Empty<byte>()` → `global::ShaRPC.Core.Buffers.Payload.Empty`.
- `Sync`/`TaskOf`/`ValueTaskOf` returns (L200/L213): `serializer.Serialize(result)` → `global::ShaRPC.Core.Serialization.SerializerExtensions.SerializeToPayload(serializer, result)`.
- sub-service null (L227): `global::ShaRPC.Core.Serialization.SerializerExtensions.SerializeToPayload<global::ShaRPC.Core.Protocol.ServiceHandle?>(serializer, null)`.
- sub-service handle (L231): `…SerializeToPayload(serializer, new global::ShaRPC.Core.Protocol.ServiceHandle { … })`.

---

## Client / Server rewrites

### `src/ShaRPC.Core/Client/ShaRpcClient.cs`
- `InvokeAsync*`: `using var inner = serializer.SerializeToPayload(request)` (or `ReadOnlyMemory<byte>.Empty` for no-body overloads); pass `inner.Memory` to `SendRequestAsync`.
- `SendRequestAsync(string service, string method, ReadOnlyMemory<byte> payload, string? instanceId, ct)`: build `RpcRequest{ Payload = payload, … }`; `using var frame = MessageFramer.FrameMessage(_serializer, messageId, MessageType.Request, request)`; `await connection.SendAsync(frame.Memory, ct)`.
- `ReceiveLoopAsync`: `using var frame = await connection.ReceiveAsync(ct)`; `if (frame.Length==0) break;` `if (!MessageFramer.TryReadFrame(frame.Memory, out id, out type, out envelope)) continue;` `var response = _serializer.Deserialize<RpcResponse>(envelope)`; `tcs.TrySetResult(response)`. Frame disposed at loop iteration end (safe — invariant above). Awaiting `InvokeAsync` later does `_serializer.Deserialize<TResponse>(response.Payload)`.
- Remove `MemoryStream`/`.ToArray()`.

### `src/ShaRPC.Core/Server/ShaRpcServer.cs`
- `HandleConnectionAsync`: `using var data = await connection.ReceiveAsync(ct)` (owns the received `Payload`); `if (data.Length==0) break;`
- `ProcessMessageAsync(connection, registry, Payload data, ct)`: `MessageFramer.TryReadFrame(data.Memory, …)`; `var request = _serializer.Deserialize<RpcRequest>(envelope)`; `using var result = await dispatcher.DispatchAsync(method, request.Payload, _serializer, registry, ct)` (or `DispatchOnInstanceAsync`); on error build error `RpcResponse`; build `RpcResponse{ Payload = result.Memory }`; `using var responseFrame = MessageFramer.FrameMessage(_serializer, messageId, responseType, response)`; `await connection.SendAsync(responseFrame.Memory, ct)`. Frame the response **before** `result` is disposed (the `using` scope ordering handles this). Remove `MemoryStream`/`.ToArray()`.

---

## Tests

- **New shared `tests/ShaRPC.SourceGenerator.Tests/TestJsonSerializer.cs`** — `internal sealed class TestJsonSerializer : ISerializer` (`using System.Buffers;`, `using System.Text.Json;`), `s_options = new(){ IncludeFields = true }`:
  - `Serialize<T>(IBufferWriter<byte> w, T v)` ⇒ `using var jw = new Utf8JsonWriter(w); JsonSerializer.Serialize(jw, v, s_options);`
  - `Deserialize<T>(ReadOnlyMemory<byte> d)` ⇒ `JsonSerializer.Deserialize<T>(d.Span, s_options)!`
  - `object? Deserialize(ReadOnlyMemory<byte> d, Type t)` ⇒ `JsonSerializer.Deserialize(d.Span, t, s_options)`
  Replace the **6** duplicated `JsonSerializerWrapper` doubles in: `BehavioralTests.cs`, `GeneratedRoundTripTests.cs` (also keep its `LoopbackClient`, but migrate its `DispatchAsync` calls — see pattern), `NestedServiceTests.cs`, `NullableSubServiceRuntimeTests.cs`, `ReviewedNestedValueTaskRuntimeTests.cs`, `CustomSubServiceWireNameRuntimeTests.cs`.

- **Dispatch call-site migration pattern** (apply across the files above):
  ```csharp
  using var p = serializer.SerializeToPayload(x);                 // or ReadOnlyMemory<byte>.Empty
  using var reply = await dispatcher.DispatchAsync(m, p.Memory, serializer, reg, ct);
  serializer.Deserialize<T>(reply.Memory);
  ```
  `Array.Empty<byte>()` → `System.ReadOnlyMemory<byte>.Empty`; `bytes.Should().BeEmpty()` / `pingBytes.Should().BeEmpty()` → `reply.Length.Should().Be(0)`. `GeneratedRoundTripTests.LoopbackClient` Invoke overloads: `Resolve(svc).DispatchAsync(method, _serializer.SerializeToPayload(request).Memory, …)` then `using` the returned `Payload` and `Deserialize<TResponse>(reply.Memory)` (dispose the inner request payload too). Null sub-service in `NullableSubServiceRuntimeTests` deserializes via `Deserialize<ServiceHandle?>(reply.Memory)`.

- **`CodegenRegressionTests.cs` ~L713**: replace `NotContain("serializer.Serialize(result)")` with `dispatcher.Should().NotContain("SerializeToPayload")` (the sync-sub-service rejection path must emit no serialization).

- **`tests/ShaRPC.Tests/MessageFramerTests.cs`**: `Frame` → `using var frame = MessageFramer.FrameToPayload(...)`, read via `frame.Span`/`frame.Memory.Span` (`BitConverter`/length/`frame[8]` assertions read the span); `ReadMessageAsync` now yields `FramedMessage?` with disposable `Payload Payload` — `using` it and compare `result.Value.Payload.Memory.ToArray()`. `WriteMessageAsync` signature unaffected. Can also add a `TryReadFrame` round-trip test.

- **Dispatcher snapshots (Verify)**: framework is VerifyXunit + VerifySourceGenerators (`ModuleInitializer.cs`, `DiffRunner.Disabled=true`). After the generator change, re-baseline the `*.ShaRpcDispatcher.g.verified.cs` files (SingleMethod, MixedReturns, CustomNames, TwoServices IOne+ITwo, ValueTaskReturns, KeywordEscapedParameters, RefOutStub, InheritedMembers, NestedServiceReturn IRootSnap+ISubSnap) by running the tests and promoting `*.received.cs` (`dotnet verify accept -y`, or delete-verified + rerun). Proxy/async snapshots stay unchanged.

- **`tests/ShaRPC.Tests/IntegrationTests.cs`**: no edits — real-TCP end-to-end over the generated proxy + `MessagePackRpcSerializer`; this is the primary regression that the whole pooled path still works.

- **Samples**: no edits (only construct `MessagePackRpcSerializer` + builders).

---

## Future Work — DONE (un-nested wire format)
Eliminated the last per-message allocation (the array MessagePack allocated to back the nested
`RpcRequest/RpcResponse.Payload`). Chose **option (b) — un-nested wire format** =
`header + envelope-length + envelope(no payload) + raw trailing payload bytes`, so the payload is a
zero-copy slice of the frame buffer.

Implemented:
- `MessageFramer`: added `EnvelopeLengthSize`; `FrameMessage<T>(serializer, id, type, envelope, ReadOnlySpan<byte> payload)` and `TryReadFrame(... out envelope, out payload)`.
- Dropped the `Payload` member from `RpcRequest`/`RpcResponse` entirely (Core has no MessagePack dependency, so attribute-based exclusion was not serializer-agnostic — the member is removed structurally).
- Server `ProcessMessageAsync` passes the zero-copy `payload` slice to the dispatcher (which deserializes args synchronously before its first `await`, so the slice never outlives the live `data` frame) and appends `resultPayload` as trailing bytes.
- Client carries the rented frame to the awaiting caller via a `ReceivedResponse : IDisposable` carrier; a `consumed` flag + `DisposeResultWhenAvailable` continuation + receive-loop `handedOff` flag make the frame-lifetime hand-off leak-proof across timeout/cancel/duplicate/error races (`Payload.Dispose` is idempotent).
- Tests: rewrote the framer round-trip test for the split format, added an empty-payload round-trip and an allocation-regression test asserting parse cost does not scale with payload size.

Verified: `dotnet build` green; 22 ShaRPC.Tests (incl. real-TCP + in-memory pipe round-trips) and 192 SourceGenerator.Tests pass with no snapshot re-baselining.

---

## Verification
1. `dotnet build -c Debug` — green (netstandard2.1 + generator + tests).
2. `dotnet test tests/ShaRPC.SourceGenerator.Tests` — generator unit/behavioral/runtime/snapshot tests (accept dispatcher snapshots once, confirm they re-pass).
3. `dotnet test tests/ShaRPC.Tests` — `MessageFramerTests` (new `Payload`/`FramedMessage` API) and **`IntegrationTests`** (real-TCP round-trip = end-to-end proof the pooled client/server/serializer/transport all agree).
4. Spot-audit: `grep -rn "new byte\[" src` returns only justified spots (none expected in the runtime path); confirm no `MemoryStream`/`.ToArray()` left in `ShaRpcClient`/`ShaRpcServer`.
5. After green, spawn the *Future Work* zero-alloc-receive task.