# 12 — Resource Limits

## Purpose

API sandboxing prevents unauthorized effects. Resource limits prevent accidental or malicious denial-of-service.

Resource control is harder than API control, especially in-process.

## Resource categories

Track at least:

```text
Fuel / instruction budget
Wall-clock time
CPU time optional
Call depth
Loop iterations
Allocated bytes estimated/cooperative
Collection element count
String/bytes size
Host call count
File bytes read/written
Network bytes read/written
Log events
Generated commands
```

## Budget model

```csharp
public sealed class ResourceBudget
{
    public long MaxFuel { get; }
    public TimeSpan MaxWallTime { get; }
    public long MaxAllocatedBytes { get; }
    public int MaxCallDepth { get; }
    public int MaxHostCalls { get; }
    public int MaxListLength { get; }
    public int MaxMapEntries { get; }
    public int MaxCollectionDepth { get; }
    public long MaxTotalCollectionElements { get; }
    public long MaxFileBytesRead { get; }
    public long MaxFileBytesWritten { get; }
    public long MaxNetworkBytesRead { get; }
    public int MaxLogEvents { get; }
    public int MaxLogMessageLength { get; }
    public int MaxStringLength { get; }
    public long MaxTotalStringBytes { get; }

    public void ChargeFuel(long amount);
    public void ChargeAllocation(long bytes);
    public void ChargeCollection(SandboxValue value);
    public void ChargeValue(SandboxValue value);
    public void ChargeString(string value);
    public void ChargeLogEvent(string message);
    public void ChargeHostCall(string bindingId, int? maxCallsPerRun = null);
}
```

All configured resource limits and binding cost values must be non-negative. Zero is allowed
where it is useful to deny a category completely, such as zero host calls, zero file bytes written,
or zero log events.

## Fuel

Fuel is the main cooperative CPU limit.

Charge fuel for:

- instructions
- branches
- function calls
- loop backedges
- collection operations
- string operations
- host calls
- parsing/formatting

Fuel exhausted result:

```text
SandboxError.QuotaExceeded("fuel exhausted")
```

## Wall-clock time

Use deadline checks:

```csharp
if (Stopwatch.GetTimestamp() > deadline) throw Timeout;
```

Do not check wall-clock on every tiny operation if too expensive. Combine with fuel:

```text
Every N fuel charges, check cancellation/deadline.
```

## Cancellation

Every execution gets a cancellation token.

Interpreter checks it regularly.

Compiled code checks via `ChargeFuel` and host binding calls.

## Memory limits

In-process memory limiting is cooperative.

Do not claim it is a hard sandbox.

### Cooperative memory controls

- route collections through sandbox collection types
- charge for collection growth
- charge for string/bytes creation
- limit return sizes from host APIs
- reject huge constants during validation
- limit nested data depth
- avoid exposing arbitrary `newarr`/`newobj`

### Hard memory controls

Use a worker process/container/OS limit for true memory caps.

Examples:

- container memory limit
- Linux cgroups
- Windows Job Object memory limits
- restricted worker account

## Allocation rules for compiled code

Generated code should not be able to allocate arbitrary objects.

Allow only:

- sandbox value constructors/factories
- approved small arrays if needed for binding args
- generated record/value types if verifier understands them

Reject/avoid:

- arbitrary `newobj`
- arbitrary `newarr`
- `StringBuilder` unless wrapped
- LINQ allocation patterns
- unbounded recursion

## Collection limits

Policy example:

```json
{
  "maxListLength": 10000,
  "maxMapEntries": 10000,
  "maxNestedDepth": 32,
  "maxTotalCollectionElements": 100000
}
```

Every collection creation or growth operation checks these. Entrypoint inputs are checked
before execution so the host cannot bypass quotas with prebuilt sandbox collections.

## String and bytes limits

Policy example:

```json
{
  "maxStringLength": 65536,
  "maxBytesLength": 1048576,
  "maxTotalStringBytes": 1048576
}
```

Reject huge string constants at validation time.

Runtime must also check strings produced after validation: string literals as they are
evaluated, entrypoint inputs, nested strings inside sandbox collections, string
concatenation, and strings returned by host APIs such as file/network bindings.

## Host call limits

Track:

- total host calls
- calls per binding
- external call durations
- bytes in/out

Policy example:

```json
{
  "maxHostCalls": 100,
  "perBindingLimits": {
    "file.readText": { "maxCalls": 10, "maxBytes": 262144 },
    "http.get": { "maxCalls": 3, "timeoutMs": 1000 }
  }
}
```

The binding descriptor can also declare a per-binding `MaxCallsPerRun` cost-model value.
That limit is enforced in the same host-call accounting path as the global `maxHostCalls`
limit, including compiled runtime binding stubs.

## Log limits

Policy example:

```json
{
  "maxLogEvents": 100,
  "maxLogMessageLength": 4096
}
```

Every sandbox-visible log operation checks these before writing a sanitized audit event.

## IO limits

File/network APIs must enforce:

- max request/write size
- max response/read size
- max total bytes per run
- timeout
- allowed resource scope

Never rely on script cooperation for IO limits.

## Command limits

If sandbox emits game/business commands, limit:

- number of commands
- command size
- target scope
- duplicate commands
- high-risk command kinds

Host validates commands after execution before applying them.

## Process boundary mode

For high-risk untrusted code, execute in a worker process.

```text
Main process
  - validates high-level request
  - sends canonical IR/plan/input to worker
  - receives result/commands/audit summary

Worker process
  - runs interpreter/compiled backend
  - has restricted OS permissions
  - has memory/time limits
  - can be killed safely
```

## Worker isolation recommendations

Use as applicable:

- separate OS user
- no access to secrets
- limited filesystem ACLs
- restricted working directory
- no inherited handles
- disabled/unavailable network unless needed
- egress firewall for network capability
- process/job/container memory limits
- wall-time watchdog
- clean worker recycle after N runs or suspicious failures

## Resource accounting parity

Interpreter and compiled mode must charge comparable budgets.

They do not need bit-perfect fuel equality, but policies must not become dramatically weaker in compiled mode.

Approach:

- define cost per IR operation
- interpreter charges during execution
- compiler injects calls matching IR operation costs
- differential tests compare resource usage within tolerance

## Failure handling

On limit exceeded:

- stop execution
- do not apply side-effect commands emitted after failure
- return `QuotaExceeded`/`Timeout`
- audit resource usage and limit hit
- optionally mark module as abusive after repeated failures

## Important limitation

In-process resource limits are not a full defense against malicious code if the verifier/compiler has a bug. Process boundary is the hard stop.
