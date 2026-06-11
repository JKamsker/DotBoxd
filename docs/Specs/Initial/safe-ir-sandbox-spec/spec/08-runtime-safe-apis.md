# 08 — Runtime Safe APIs

## Purpose

Runtime safe APIs are the only way sandboxed code reaches host resources.

They are narrow, capability-aware, budget-aware, and auditable.

## `SandboxContext`

`SandboxContext` is the execution root passed to interpreter and compiled code.

It should provide:

```csharp
public sealed class SandboxContext
{
    public SandboxRunId RunId { get; }
    public SandboxPolicy Policy { get; }
    public ResourceBudget Budget { get; }
    public BindingTable Bindings { get; }
    public IAuditSink Audit { get; }
    public CancellationToken CancellationToken { get; }

    public void RequireCapability(string capabilityId);
    public CapabilityGrant GetCapability(string capabilityId);
    public void ChargeFuel(int amount);
    public void ChargeAllocation(long bytes);
}
```

Do not expose service providers, host containers, or arbitrary host state from `SandboxContext`.

## Safe file API

### Goals

- scoped paths only
- no absolute paths from user
- no path traversal
- no direct `System.IO.File` access from IR/generated code
- enforce size quotas
- audit file access

### Sandbox path

Use a `SandboxPath` value, not raw OS paths.

```text
SandboxPath = normalized relative path inside a named root
```

Examples:

```text
config/settings.json
assets/icon.png
```

Reject:

```text
../secret.txt
C:\Windows\win.ini
/etc/passwd
\\server\share\file
file://...
```

### Safe facade

```csharp
public sealed class SafeFileSystem
{
    public ValueTask<string> ReadTextAsync(
        SandboxContext ctx,
        SandboxPath path,
        CancellationToken ct);

    public ValueTask WriteTextAsync(
        SandboxContext ctx,
        SandboxPath path,
        string text,
        CancellationToken ct);
}
```

### Required checks

For read:

1. require `file.read`
2. resolve named root from capability grant
3. reject absolute paths
4. normalize separators
5. combine root + relative path
6. canonicalize full path
7. ensure final path remains inside root
8. reject disallowed extension/glob
9. check file size before read if possible
10. enforce max bytes per file/run
11. charge fuel/allocation
12. audit sanitized path

For write:

1. require `file.write`
2. same path checks
3. enforce create/overwrite policy
4. write atomically when possible
5. enforce max bytes
6. audit

### Symlink/reparse-point warning

Path prefix checks alone can be insufficient in the presence of symlinks, junctions, or reparse points.

Defense options:

- reject symlinks/reparse points in sandbox roots
- resolve final real path when possible
- open handles safely and validate after open
- use OS-level filesystem permissions for the worker account
- use container/chroot-like isolation for high-risk tenants

## Safe network API

Network should be disabled by default.

Facade:

```csharp
public sealed class SafeHttpClient
{
    public ValueTask<SandboxHttpResponse> GetAsync(
        SandboxContext ctx,
        SandboxUri uri,
        CancellationToken ct);
}
```

Required checks:

- require `net.http.get` or similar
- allow only configured schemes, usually `https`
- allow only configured hosts
- reject IP literals unless explicitly allowed
- reject localhost/private ranges unless explicitly allowed
- enforce DNS pinning/rebinding protections for every real network request
- enforce request/response size limits
- enforce the stricter of the request timeout and remaining sandbox wall-time budget
- audit host/path sanitized

The production transport must own the real network path. Host code may provide an explicit
in-memory HTTP invoker for tests and deterministic fixtures, but arbitrary `HttpClient`,
`HttpMessageHandler`, socket, stream, or handler injection is not part of the sandbox API.
Opaque custom transports cannot prove that they connected to the vetted DNS/IP target and
must not be treated as pinned network execution.

Do not expose raw `HttpClient`, sockets, streams, handlers, or headers that can smuggle credentials unless explicitly modeled.

## Safe game-state API

Do not expose live mutable game objects.

Use snapshots and commands.

Read example:

```csharp
public sealed record PlayerSnapshot(
    PlayerId Id,
    string Name,
    int Level,
    int Health,
    IReadOnlyList<ItemSnapshot> Inventory);
```

Mutation example:

```csharp
public sealed record InventoryCommand(
    PlayerId PlayerId,
    ItemId ItemId,
    int Amount,
    string Reason);
```

Sandbox execution produces commands:

```text
commands = ExecuteQuestLogic(snapshot)
host validates commands in transaction
host applies commands
```

This avoids direct mutation during sandbox execution.

## Safe database API

Prefer not exposing database APIs.

Instead expose domain-specific query bindings:

```text
game.quest.getProgress(playerId, questId)
game.item.getDefinition(itemId)
```

Avoid:

- raw SQL
- LINQ/IQueryable
- DbContext
- connection objects
- transaction objects

If database-like queries are needed, define a small JSON query IR with row/byte/time limits.

## Safe clock API

Clock is an effect.

```csharp
public interface ISandboxClock
{
    DateTimeOffset UtcNow(SandboxContext ctx);
}
```

In deterministic mode, clock returns policy-provided logical time or is unavailable.

## Safe random API

Randomness is an effect.

```csharp
public interface ISandboxRandom
{
    int NextInt32(SandboxContext ctx, int minInclusive, int maxExclusive);
}
```

Policy decides:

- deterministic seeded random
- nondeterministic random
- forbidden random

For game/economy/security behavior, prefer host-owned randomness outside the sandbox.

## Safe collections

Sandbox collections enforce allocation budgets.

```csharp
public sealed class SandboxList<T>
{
    public int Count { get; }
    public T Get(int index);
    public SandboxList<T> Add(SandboxContext ctx, T value);
}

public sealed class SandboxMap<TKey, TValue>
{
    public bool ContainsKey(TKey key);
    public TValue Get(TKey key);
    public SandboxMap<TKey, TValue> Set(SandboxContext ctx, TKey key, TValue value);
    public SandboxMap<TKey, TValue> Remove(SandboxContext ctx, TKey key);
}
```

Growth and copy-on-write updates charge allocation budget. Missing map keys return a safe
`NotFound` sandbox error. Unsupported map key types are rejected during validation. List
length, map entry count, nested collection depth, and total collection elements are checked
against policy resource limits.

## Safe logging API

Sandbox-visible logging should be structured and quota-limited.

```text
log.info(message)
log.warn(message)
```

Both operations require the host-granted `log.write` capability and emit sanitized
`SandboxLog` audit events.

Limits:

- max log events per run
- max message length
- no secrets
- audit correlation by run ID

## Safe error API

Use sandbox error values:

```text
PermissionDenied
InvalidInput
NotFound
QuotaExceeded
Timeout
Cancelled
HostFailure
```

Do not leak raw host exceptions to untrusted code.

## Facade implementation principles

Every safe API must:

- require capability when effectful
- charge fuel/alloc/IO budgets
- accept cancellation
- enforce timeouts for external operations
- sanitize errors
- emit audit events when needed
- avoid returning raw host objects
- be deterministic when marked deterministic

## Anti-patterns

Do not expose:

```csharp
IServiceProvider
Func<string, object>
object Invoke(string name, object[] args)
Type GetType(string name)
Assembly Load(string name)
Stream Open(string path)
HttpClient Client
DbContext Db
Player LivePlayer
```

The sandbox should expose verbs, not power tools.
