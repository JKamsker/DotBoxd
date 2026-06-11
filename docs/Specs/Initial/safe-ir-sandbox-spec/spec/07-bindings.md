# 07 — Bindings

## Purpose

Bindings connect sandbox operations to host-provided implementations.

A binding is not just a method pointer. It is a security contract.

It declares:

- stable sandbox ID
- version
- argument and return sandbox types
- effects
- required capability
- resource costs
- audit behavior
- interpreter implementation
- compiled-mode implementation/stub
- safety classification

## Rule: only the host grants bindings

Script authors may request capabilities. They must never register or grant bindings.

Correct:

```text
JSON IR: capabilityRequests contains file.read
Host:   binds file.readText -> SafeFileSystem.ReadText
Policy: grants file.read for tenant config root
```

Incorrect:

```text
Script: bind file.readText = System.IO.File.ReadAllText
```

That is not a sandbox.

## Binding descriptor

```csharp
public sealed record BindingDescriptor(
    string Id,
    SemVersion Version,
    IReadOnlyList<SandboxType> Parameters,
    SandboxType ReturnType,
    SandboxEffect Effects,
    string? RequiredCapability,
    BindingCostModel CostModel,
    AuditLevel AuditLevel,
    BindingSafety Safety,
    BindingInvoker Invoke,
    CompiledBinding Compiled);
```

## Binding safety levels

```csharp
public enum BindingSafety
{
    PureIntrinsic,
    PureHostFacade,
    ReadOnlyExternal,
    SideEffectingExternal,
    DangerousRequiresReview
}
```

Rules:

- `DangerousRequiresReview` cannot be enabled by default.
- side-effecting bindings require capabilities.
- pure bindings must be reviewed for hidden side effects.

## Binding manifest

A binding manifest should be serializable and hashable.

Example:

```json
{
  "id": "file.readText",
  "version": "1.0.0",
  "parameters": ["SandboxPath"],
  "returnType": "String",
  "effects": ["Cpu", "Alloc", "FileRead"],
  "requiredCapability": "file.read",
  "costModel": {
    "baseFuel": 50,
    "perByteFuel": 1,
    "allocationFromReturnBytes": true
  },
  "auditLevel": "PerResource",
  "safety": "ReadOnlyExternal",
  "compiledTarget": {
    "kind": "RuntimeStub",
    "type": "Sandbox.Runtime.GeneratedBindings.FileBindings",
    "method": "ReadText"
  }
}
```

## Binding implementation model

### Interpreter implementation

The interpreter calls a delegate that accepts sandbox values:

```csharp
public delegate ValueTask<SandboxValue> BindingInvoker(
    SandboxContext context,
    IReadOnlyList<SandboxValue> args,
    CancellationToken cancellationToken);
```

Benefits:

- one code path for policy/resource checks
- easy audit
- no generated code required

### Compiled implementation

Compiled code should call stable runtime stubs, not arbitrary host methods.

Preferred:

```csharp
public static class CompiledRuntime
{
    public static SandboxValue CallBinding(
        SandboxContext ctx,
        string bindingId,
        SandboxValue[] args)
        => ctx.Bindings.InvokeKnown(bindingId, args);
}
```

For performance, pure sandbox intrinsics may point at exact reviewed `CompiledRuntime` methods
such as `AbsI32` or `StringLength`. External host facades must route through the generic
binding-call stub. The registry must validate the exact compiled target type and method; a
namespace prefix such as `SafeIR.Runtime.*` is not enough.

Avoid generating code that directly calls arbitrary app methods.

## Why stubs are safer than arbitrary method refs

If compiled code can call any registered CLR method directly, the verifier allowlist becomes large and fragile.

A smaller surface is better:

```text
Generated code may call:
  Sandbox.Runtime.GeneratedBindingStubs.Call(ctx, bindingSlot, args)

Generated code may not call:
  MyGameServer.PlayerService.Whatever(...)
  System.IO.File.ReadAllText(...)
  IServiceProvider.GetService(...)
```

The binding slot maps to a descriptor in the execution plan.

## Binding signature validation

Reject bindings with forbidden parameter or return types:

- `object`
- `dynamic`
- `Type`
- reflection types
- `Delegate`
- `Expression`
- `IServiceProvider`
- `Stream`
- `DbContext`
- `HttpClient`
- `IntPtr`
- host domain entities with behavior
- mutable collections not owned by sandbox

Require bindings to use:

- sandbox primitive types
- sandbox values
- immutable DTO snapshots
- opaque IDs
- command objects
- safe facades

## Host facade pattern

Bad binding:

```csharp
sandbox.Bind("file.readText", typeof(File).GetMethod("ReadAllText", ...));
```

Good binding:

```csharp
sandbox.Bind("file.readText", SafeFileBindings.ReadTextDescriptor);
```

Where implementation is:

```csharp
public sealed class SafeFileSystem
{
    public ValueTask<string> ReadTextAsync(
        SandboxContext ctx,
        SandboxPath path,
        CancellationToken ct)
    {
        ctx.RequireCapability("file.read");
        ctx.Budget.ChargeFuel(50);
        return ReadScopedFileAsync(ctx, path, ct);
    }
}
```

## Binding lifecycle

Bindings are versioned.

Changing any of these requires version/hash update:

- parameter types
- return type
- effects
- required capability
- cost model
- per-run call limit
- compiled target
- safety classification
- semantics

## Binding registry

The binding registry is built by trusted host code:

```csharp
var registry = new BindingRegistryBuilder()
    .Add(SandboxMathBindings.All)
    .Add(SandboxCollectionBindings.All)
    .Add(SafeFileBindings.ReadOnly)
    .Build();
```

Registry build should validate:

- duplicate IDs
- duplicate slot numbers
- forbidden signatures
- missing capability for effects
- negative resource costs or per-binding call limits
- unknown types
- unknown effects
- invalid compiled stubs
- compiled stub targets are exact reviewed `CompiledRuntime` methods
- direct runtime methods are used only by pure intrinsic bindings

## Default bindings

Default pure bindings and sandbox intrinsics:

```text
math.abs
math.min
math.max
math.clamp
math.sqrt
math.sin/cos/tan optional
math.floor/ceil/round
string.length
string.concatBudgeted
list.empty
list.of
list.count
list.get
list.add
map.empty
map.containsKey
map.get
map.set
map.remove
parse.int optional
format.simple optional
```

Default side-effect bindings should be disabled unless policy grants them.

Default audited logging bindings:

```text
log.info  capability: log.write
log.warn  capability: log.write
```

## Bindings for IO rewrite

The JSON IR should use semantic operation IDs:

```json
{ "call": "file.readText", "args": [{ "path": "config/settings.json" }] }
```

The host maps that to:

```text
binding id: file.readText
capability: file.read
implementation: SafeFileSystem.ReadText
```

There is no point where the user references `System.IO.File`.

## Binding errors

Host binding errors should be converted to sandbox errors.

Example mapping:

| Host condition | Sandbox error |
|---|---|
| capability missing | `PermissionDenied` |
| path outside root | `PermissionDenied` |
| file missing | `NotFound` |
| file too large | `QuotaExceeded` |
| timeout | `Timeout` |
| cancellation | `Cancelled` |
| unexpected binding exception | `BindingFailure` with safe message |

Do not leak secrets, full paths, connection strings, stack traces, or internal object details to untrusted users.

## Binding audit

Side-effecting or external-read bindings must emit audit events.

Audit event fields:

```text
runId
moduleHash
policyHash
bindingId
capabilityId
resourceId/path/url/entityId sanitized
effect
startedAt
completedAt
duration
success/failure
bytesRead/bytesWritten optional
```

## Binding review checklist

A binding is acceptable only if:

- it uses allowed sandbox-visible types
- it declares all effects
- it has a capability for side effects
- it charges fuel/allocation/IO budgets
- it has deterministic behavior or is marked nondeterministic
- it logs required audit events
- it does not return raw host objects
- it does not accept service locators
- it does not expose reflection/runtime/native handles
- it handles cancellation/timeouts
- it sanitizes exceptions
