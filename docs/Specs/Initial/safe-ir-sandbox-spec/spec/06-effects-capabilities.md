# 06 — Effects and Capabilities

## Purpose

Effects describe what code may do. Capabilities are host-granted permissions to perform effects.

The sandbox should answer before execution:

```text
What effects can this module perform?
Are those effects granted by policy?
What parameters constrain those effects?
```

## Effects vs capabilities

An effect is a category of behavior:

```text
FileRead
FileWrite
Network
HostStateRead
HostStateWrite
Random
Time
Alloc
Cpu
```

A capability is a grant:

```text
file.read root=tenant-data maxBytes=1MB
net.http.get hosts=[api.example.com] timeout=1s
```

Effects are inferred from IR. Capabilities are granted by host policy.

## Base effect enum

```csharp
[Flags]
public enum SandboxEffect
{
    None = 0,
    Cpu = 1 << 0,
    Alloc = 1 << 1,
    Time = 1 << 2,
    Random = 1 << 3,
    FileRead = 1 << 4,
    FileWrite = 1 << 5,
    Network = 1 << 6,
    HostStateRead = 1 << 7,
    HostStateWrite = 1 << 8,
    DatabaseRead = 1 << 9,
    DatabaseWrite = 1 << 10,
    Audit = 1 << 11
}
```

This enum is not enough by itself. Capabilities need parameters.

## Capability grants

```csharp
public sealed record CapabilityGrant(
    string Id,
    IReadOnlyDictionary<string, CapabilityParameter> Parameters,
    DateTimeOffset? ExpiresAt,
    string GrantedBy,
    string Reason);
```

Examples:

```json
{
  "id": "file.read",
  "parameters": {
    "roots": ["/srv/safe-ir/tenants/123/config"],
    "maxBytesPerRun": 1000000,
    "allowGlobs": ["*.json", "*.txt"]
  },
  "grantedBy": "server-policy",
  "reason": "Plugin reads tenant config"
}
```

```json
{
  "id": "game.inventory.write",
  "parameters": {
    "allowedItemKinds": ["cosmetic", "quest"],
    "maxCommandsPerRun": 10,
    "requiresTransaction": true
  },
  "grantedBy": "server-owner",
  "reason": "Quest plugin reward flow"
}
```

## Capability requests

A module can declare requested capabilities:

```json
{
  "capabilityRequests": [
    { "id": "file.read" },
    { "id": "game.character.read" }
  ]
}
```

Requests can include reasons:

```json
{
  "capabilityRequests": [
    { "id": "file.read", "reason": "Load plugin config" }
  ]
}
```

Requests cannot include trusted parameters unless the host accepts them as hints.

Bad:

```json
{
  "capabilityRequests": [
    { "id": "file.read", "root": "C:\\" }
  ]
}
```

Good:

```json
{
  "capabilityRequests": [
    { "id": "file.read", "reason": "Need tenant-local config" }
  ]
}
```

## Effect inference

Every operation has an effect signature.

Examples:

| Operation | Effects |
|---|---|
| `add I32` | `Cpu` |
| `list.empty` / `list.of` | `Cpu | Alloc` |
| `list.add` | `Cpu | Alloc` |
| `map.empty` / `map.set` / `map.remove` | `Cpu | Alloc` |
| `list.count` / `list.get` / `map.containsKey` / `map.get` | `Cpu` |
| `math.sqrt` | `Cpu` |
| `clock.now` | `Cpu | Time` |
| `random.next` | `Cpu | Random` |
| `file.readText` | `Cpu | Alloc | FileRead` |
| `log.info` / `log.warn` | `Cpu | Audit` |
| `game.inventory.grant` | `Cpu | HostStateWrite | Audit` |

Function effects are the union of all reachable operation effects.

## Policy resolution

Policy resolution checks:

1. all required capabilities are granted
2. no forbidden effects are present
3. capability parameters satisfy binding requirements
4. deterministic mode is respected
5. audit requirements are configured
6. resource budgets are sufficient and bounded

Example policy:

```json
{
  "policyId": "tenant-basic-readonly-v1",
  "allowedEffects": ["Cpu", "Alloc", "FileRead", "HostStateRead"],
  "capabilities": [
    {
      "id": "file.read",
      "parameters": {
        "roots": ["tenant://self/config"],
        "maxBytesPerRun": 262144
      }
    },
    {
      "id": "game.character.read",
      "parameters": {
        "scope": "current-player-only"
      }
    }
  ],
  "budgets": {
    "maxFuel": 100000,
    "maxWallTimeMs": 50,
    "maxAllocatedBytes": 1048576
  }
}
```

## Deny-by-default

If an effect/capability is unknown, reject.

If a binding has no effect declaration, reject.

If a policy has an unknown grant, reject or ignore with diagnostics; do not silently allow.

## Capability hierarchy

Avoid broad grants where possible.

Bad:

```text
grant file.*
grant game.*
grant network
```

Good:

```text
grant file.read root=tenant-config maxBytes=256KB
grant game.character.read scope=current-player
grant http.get host=api.example.com maxResponse=64KB
```

## Scoped capabilities

Recommended capability scope parameters:

### Files

```text
root
readOnly
allowedExtensions
allowedGlobs
maxBytesPerFile
maxBytesPerRun
allowCreate
allowOverwrite
```

### Network

```text
allowedHosts
allowedSchemes
allowedMethods
maxRequestBytes
maxResponseBytes
timeoutMs
rateLimit
```

### Game state

```text
tenantId
serverId
currentPlayerOnly
allowedEntityKinds
allowedCommandKinds
maxCommandsPerRun
requiresTransaction
```

### Database

Prefer not exposing DB at all. Expose domain-specific query bindings instead.

If absolutely required:

```text
readModelName
queryIds
maxRows
maxBytes
noRawSql=true
```

## Determinism

Policy can require deterministic mode:

```json
{
  "deterministic": true
}
```

In deterministic mode:

- `time.now` must be replaced with a supplied logical time or rejected
- `random` must use a policy-provided seed or be rejected
- network and external IO should be rejected unless modeled as deterministic inputs
- unordered map iteration must be stable

## Audit classification

Each capability should declare audit level:

```text
None
Summary
PerCall
PerResource
FullInputOutput optional/sensitive
```

Examples:

| Capability | Audit level |
|---|---|
| `math.sqrt` | `None` |
| `file.read` | `PerResource` |
| `game.inventory.write` | `PerCall` |
| `net.http.post` | `PerCall` |

## Capability revocation

When a capability is revoked:

- existing execution plans requesting or reaching it must fail before execution
- compiled cache entries for affected plans must not be reused after the revoke
- running executions may be cancelled depending on policy
- audit log should record the revocation reason

This is why the policy hash must be part of the execution-plan and compiled-DLL cache key,
and why the host also keeps a runtime revocation gate for already-prepared plans.
