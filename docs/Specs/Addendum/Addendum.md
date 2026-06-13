# Addendum: Live Kernels, Value Bindings, and Server-Side Hook Pipelines

## Status

Accepted for the current SafeIR plugin model. The local SDK/analyzer/generator and server-side
package installation paths implement the JSON Safe IR package boundary described here; future
extensions should update this addendum rather than treating it as merely proposed.

## Purpose

This addendum extends the original Safe IR Sandbox specification with a higher-level plugin developer model for:

* shared server-provided plugin contracts
* plugin-authored kernel classes
* live runtime-adjustable values
* server-side hook/filter pipelines
* hot-path plugin execution without IPC
* safe local authoring using constrained C# lowered to IR

The goal is to make plugin development feel like normal C# while preserving the original spec’s core security model:

> The server never executes arbitrary plugin DLLs.
> Plugin code is lowered into Safe IR, verified by the server, and executed through approved runtime modes.

The local examples may reference a generated package factory from a plugin project to demonstrate
the build-time lowering flow. That factory is trusted development-time code, not the production
upload boundary. A production server should receive serialized JSON package data containing Safe
IR plus manifest metadata and validate it before installation. The generated package factory can
create a `PluginPackage`; `PluginPackageJsonSerializer.Export` converts that package to the JSON
envelope used for upload.

---

# 1. High-Level Model

The server provides a shared contract assembly.

Plugin developers reference that assembly and implement approved interfaces.

```text
┌──────────────────────────────┐
│ Game / App Server             │
│                              │
│ Provides shared contracts     │
│ and safe plugin APIs          │
└──────────────┬───────────────┘
               │
               │ NuGet / SDK package
               ▼
┌──────────────────────────────┐
│ Plugin Developer              │
│                              │
│ Implements interfaces         │
│ Writes constrained C#         │
│ Builds safe plugin package    │
└──────────────┬───────────────┘
               │
               │ .gameplugin package
               ▼
┌──────────────────────────────┐
│ Plugin Server                 │
│                              │
│ Validates package             │
│ Approves capabilities         │
│ Runs hooks / filters / kernels│
└──────────────────────────────┘
```

The plugin developer does not upload arbitrary executable .NET code.

They upload a JSON safe plugin package containing verified plugin behavior.

---

# 2. Shared Contracts

The server defines plugin slots as shared interfaces.

Example:

```csharp
public interface IItemFilter
{
    bool Accept(ItemView item, PlayerView player);
}

public interface IDamageFormula
{
    int Calculate(DamageInput input);
}

public interface IEventKernel<TEvent>
{
    bool ShouldHandle(TEvent e, HookContext context);

    void Handle(TEvent e, HookContext context);
}
```

The shared assembly may contain:

```text
Allowed:
  plugin interfaces
  event/view models
  readonly data records
  safe action facades
  safe helper abstractions
  plugin attributes
  configuration attributes
```

The shared assembly must not expose:

```text
Forbidden:
  DbContext
  IServiceProvider
  raw domain entities
  raw database access
  raw file/network APIs
  process/thread APIs
  reflection APIs
  arbitrary object/service escape hatches
```

The contract assembly is the plugin developer’s visible universe.

---

# 3. Plugin Developer Authoring Model

Plugin developers write normal-looking C# against approved contracts.

Example:

```csharp
[GamePlugin("epic-items-only")]
public sealed partial class EpicItemsOnly : IItemFilter
{
    public bool Accept(ItemView item, PlayerView player)
    {
        return item.Rarity >= Rarity.Epic;
    }
}
```

The current SDK generator lowers `IEventKernel<TEvent>` kernels. Other shared interfaces are
contract guidance until the host provides a matching adapter/lowering path.

For event handling:

```csharp
[GamePlugin("fire-damage")]
public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
{
    [LiveSetting]
    public string DamageType { get; set; } = "fire";

    [LiveSetting]
    public int MinDamage { get; set; } = 100;

    public bool ShouldHandle(DamageEvent e, HookContext context)
    {
        return e.DamageType == DamageType
            && e.Amount >= MinDamage;
    }

    public void Handle(DamageEvent e, HookContext context)
    {
        context.Messages.Send(e.TargetId, "Fire damage detected.");
    }
}
```

From the plugin developer perspective:

```text
I implement an interface.
I use safe event/view/context types.
I expose configurable properties.
The SDK tells me when I wrote unsupported code.
I build and upload a plugin package.
```

---

# 4. Live Kernel State

Kernel classes may expose runtime-adjustable properties by annotating them with `[LiveSetting]`.

Only annotated properties are treated as live runtime-adjustable state and mirrored into the package
manifest.

```csharp
public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
{
    [LiveSetting]
    public string DamageType { get; set; } = "fire";

    [LiveSetting]
    public int MinDamage { get; set; } = 100;

    public bool ShouldHandle(DamageEvent e, HookContext context)
    {
        return e.DamageType == DamageType
            && e.Amount >= MinDamage;
    }

    public void Handle(DamageEvent e, HookContext context)
    {
        context.Messages.Send(e.TargetId, "Fire damage detected.");
    }
}
```

Runtime update:

```csharp
kernel.Value.MinDamage = 250;
kernel.Value.DamageType = "ice";
```

Future event evaluations observe the new values.

```text
Before:

  DamageType = "fire"
  MinDamage  = 100

After update:

  DamageType = "ice"
  MinDamage  = 250

Next event:
  ShouldHandle uses the updated values.
```

Kernel properties are not arbitrary CLR fields from the server perspective.

They are live, typed, server-managed plugin state.

---

# 5. Hook Pipelines

The plugin server exposes hooks as server-side pipelines.

Example:

```csharp
server.RegisterEventAdapter(DamageEventAdapter.Instance);

server.Hooks.On<DamageEvent>()
    .UseKernel<FireDamageKernel>();
```

Conceptually:

```text
DamageEvent arrives
      │
      ▼
┌──────────────────────────┐
│ FireDamageKernel         │
│ ShouldHandle(event, ctx) │
└────────────┬─────────────┘
             │
     false ──┴── true
      │          │
      ▼          ▼
   ignore     Handle(event, ctx)
```

The server may support explicit filters:

```csharp
var minDamage = server.BindValue("minDamage", 100);

server.Hooks.On<DamageEvent>()
    .Where((e, ctx) => e.Amount >= minDamage.Value)
    .InvokeHostHandler((e, ctx) => HandleDamage(e, ctx));
```

However, kernel classes are the preferred ergonomic model for real plugins.

Hook kernel entrypoint parameters are name-bound. The verified IR entrypoints
must declare the event adapter parameters first, followed by live setting
parameters, with exact names, types, and order.
Event shape validation is owned by the trusted event adapter; the adapter must expose only approved
snapshot fields and convert them to the declared `SandboxValue` inputs.
Production servers should register reviewed event adapters before installing packages or wiring
hooks. Convention/discovery adapters are a development convenience, not the recommended production
boundary.

---

# 6. Value Bindings

A value binding is a live runtime-adjustable scalar value.

Example:

```csharp
var damageType = server.BindValue("damageType", "fire");
var minDamage = server.BindValue("minDamage", 100);

server.Hooks.On<DamageEvent>()
    .Where((e, ctx) => e.DamageType == damageType.Value)
    .Where((e, ctx) => e.Amount >= minDamage.Value)
    .InvokeHostHandler((e, ctx) => HandleDamage(e, ctx));
```

Runtime updates:

```csharp
damageType.Value = "ice";
minDamage.Value = 250;
```

Future events use the updated values.

Value bindings are suitable for:

```text
thresholds
flags
modes
names
types
rarity limits
damage limits
feature toggles
```

Value bindings are best for small/simple plugins.

For larger plugins, prefer context bindings or kernel properties.

---

# 7. Context Bindings

A context binding groups multiple live values into a typed settings object.

Example:

```csharp
public interface DamageSettings
{
    bool Enabled { get; set; }

    string DamageType { get; set; }

    int MinDamage { get; set; }
}
```

Usage:

```csharp
var settings = server.BindContext<DamageSettings>("damage");

settings.Value.Enabled = true;
settings.Value.DamageType = "fire";
settings.Value.MinDamage = 100;

server.Hooks.On<DamageEvent>()
    .Where((e, ctx) => settings.Value.Enabled)
    .Where((e, ctx) => e.DamageType == settings.Value.DamageType)
    .Where((e, ctx) => e.Amount >= settings.Value.MinDamage)
    .InvokeHostHandler((e, ctx) => HandleDamage(e, ctx));
```

Conceptually:

```text
┌──────────────────────────┐
│ DamageSettings           │
├──────────────────────────┤
│ Enabled    = true        │
│ DamageType = "fire"      │
│ MinDamage  = 100         │
└────────────┬─────────────┘
             │
             ▼
Hook filters read latest settings.
```

Context bindings are suitable for medium-complexity plugins.

Kernel properties are preferred when settings and behavior naturally belong together.

---

# 8. Kernel Classes

Kernel classes combine:

```text
live settings
filter logic
handler logic
```

This is the preferred main plugin authoring model.

Example:

```csharp
[GamePlugin("fire-damage")]
public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
{
    [LiveSetting]
    public string DamageType { get; set; } = "fire";

    [LiveSetting]
    [Range(0, 10_000)]
    public int MinDamage { get; set; } = 100;

    [LiveSetting]
    public bool Enabled { get; set; } = true;

    public bool ShouldHandle(DamageEvent e, HookContext context)
    {
        return Enabled
            && e.DamageType == DamageType
            && e.Amount >= MinDamage;
    }

    public void Handle(DamageEvent e, HookContext context)
    {
        context.Messages.Send(e.TargetId, "Fire damage detected.");
    }
}
```

Registration:

```csharp
server.RegisterEventAdapter(DamageEventAdapter.Instance);

server.Hooks.On<DamageEvent>()
    .UseKernel<FireDamageKernel>();
```

Runtime adjustment:

```csharp
var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

kernel.Value.MinDamage = 250;
kernel.Value.DamageType = "ice";
```

Direct class-kernel property assignment is `LiveUpdateMode.Sync` by default: the runtime synchronizes
typed settings into the committed `LiveSettingStore` before building sandbox input. If
`LiveUpdateMode.AsyncSet` is enabled, property assignment updates the typed object immediately, but
store synchronization is deferred. The hook execution that first sees the change may enqueue the
deferred update and still run with the previously committed store values. Call
`FlushUpdatesAsync` before publishing an event when the next run must observe the direct assignment.
Use `ModifyAsync` for validated batch commits; it ignores `UpdateMode` and completes only after the
batch has been applied.

This should be the primary user-facing story.

```text
Properties = live settings
ShouldHandle = server-side filter
Handle = action
```

---

# 9. Admin / Server Owner Experience

The server owner should see plugin state as editable settings.

Example UI:

```text
┌────────────────────────────────────┐
│ Fire Damage Plugin                 │
├────────────────────────────────────┤
│ Enabled:    [x]                    │
│ DamageType: [fire              ▼]  │
│ MinDamage:  [100               ]   │
├────────────────────────────────────┤
│ Permissions:                       │
│   ✓ Read damage events             │
│   ✓ Send player messages           │
│   ✗ File access                    │
│   ✗ Network access                 │
└────────────────────────────────────┘
```

Changing a value updates future hook evaluations.

No reinstall is required.

No hook pipeline rebuild is required from the admin perspective.

---

# 10. Runtime Configuration

Plugin settings may be initialized from server configuration.

Example:

```yaml
plugins:
  fire-damage:
    enabled: true
    mode: auto
    settings:
      DamageType: fire
      MinDamage: 100
      Enabled: true
```

The runtime may persist live changes:

```yaml
plugins:
  fire-damage:
    settings:
      DamageType: ice
      MinDamage: 250
      Enabled: true
```

The persistence mechanism is implementation-specific.

The user-facing rule is:

> Live settings affect future executions.

---

# 11. Execution Modes

Live kernels and bindings are compatible with all original execution modes.

```text
interpreted:
  Runs directly from validated IR.
  Best for rare hooks, development, debugging, and cold paths.

compiled:
  Runs from verified generated code.
  Best for hot filters, formulas, and frequent hooks.

auto:
  Starts interpreted.
  May optimize later if the plugin becomes hot.
```

From the plugin developer/server owner perspective, the behavior is identical.

```text
Same plugin.
Same settings.
Same hook.
Different execution backend.
```

Changing live settings must not require recompilation from the user’s perspective.

An implementation may internally specialize or re-optimize, but this must be transparent.

Plugin execution must remain observable across modes. Each plugin hook/kernel run should expose the
actual backend through the normal execution result and audit stream:

```text
requested mode: interpreted | compiled | auto
actual mode: interpreted | compiled
fallback reason: optional safe error code
cache/materialization status: None | Hit | Miss | Invalid | Recompiled
```

Admin/server tooling should display this as runtime status, not as a plugin permission. A plugin
must not be able to request or force compiled execution; mode selection remains a host policy.

Installed kernels expose this status through `LastExecution` and `ExecutionObservations`. Each
observation records the entrypoint, requested mode, actual mode, success flag, fallback reason when
present, cache/materialization status, and compiled runtime envelope fields when compiled execution
was used. These observations are host-owned telemetry snapshots; plugin code cannot mutate them.

---

# 12. Hook Subscription Model

A hook subscription is a live server-side registration.

Example:

```csharp
server.Hooks.On<DamageEvent>()
    .Where((e, ctx) => e.Amount >= minDamage.Value)
    .UseKernel<FireDamageKernel>();
```

Host-authored `Where` filters and `InvokeHostHandler` callbacks are trusted server delegates. They
are not portable untrusted plugin code unless a host-specific lowering path turns them into Safe IR.

Conceptually:

```text
┌────────────────────────────────────┐
│ Hook Subscription                  │
├────────────────────────────────────┤
│ Event type: DamageEvent            │
│ Filters:                           │
│   Amount >= minDamage              │
│ Kernel:                            │
│   FireDamageKernel                 │
│ Live state:                        │
│   minDamage                        │
│   FireDamageKernel settings        │
└────────────────────────────────────┘
```

Event flow:

```text
DamageEvent
   │
   ▼
server-side hook filters
   │
   ├── rejected -> stop
   │
   ▼
kernel.ShouldHandle
   │
   ├── false -> stop
   │
   ▼
kernel.Handle
```

---

# 13. Recommended Ergonomic Levels

The product should support three ergonomic levels.

## Level 1: Value Bindings

Best for tiny plugins.
This is a host-side convenience pattern; it does not produce a portable plugin package by itself.

```csharp
var minDamage = server.BindValue("minDamage", 100);

server.Hooks.On<DamageEvent>()
    .Where((e, ctx) => e.Amount >= minDamage.Value)
    .InvokeHostHandler((e, ctx) => Handle(e, ctx));
```

## Level 2: Context Bindings

Best for grouped settings.
This is also host-side configuration plumbing; package generation starts at the kernel-class model.

```csharp
var settings = server.BindContext<DamageSettings>("operatorDefaults");

server.Hooks.On<DamageEvent>()
    .Where((e, ctx) => settings.Value.Enabled)
    .Where((e, ctx) => e.Amount >= settings.Value.MinDamage)
    .InvokeHostHandler((e, ctx) => Handle(e, ctx));
```

## Level 3: Kernel Classes

Best for real plugins.

```csharp
public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
{
    [LiveSetting]
    public int MinDamage { get; set; } = 100;

    public bool ShouldHandle(DamageEvent e, HookContext ctx)
    {
        return e.Amount >= MinDamage;
    }

    public void Handle(DamageEvent e, HookContext ctx)
    {
        // handle
    }
}
```

The main documentation should lead with Level 3 because it is the package/generator-supported
authoring path. Levels 1 and 2 remain useful for host-authored hook composition.

---

# 14. Plugin Package Manifest Additions

The production package is a JSON envelope with a manifest and a Safe IR module:

```json
{
  "manifest": {
    "pluginId": "fire-damage",
    "contract": "IEventKernel<DamageEvent>",
    "mode": "auto",
    "effects": ["Cpu", "Alloc"],
    "liveSettings": [],
    "subscriptions": [
      { "event": "DamageEvent", "kernel": "FireDamageKernel" }
    ]
  },
  "entrypoints": {
    "shouldHandle": "ShouldHandle",
    "handle": "Handle"
  },
  "module": {
    "id": "fire-damage",
    "version": "1.0.0",
    "targetSandboxVersion": "1.0.0",
    "capabilityRequests": [],
    "metadata": { "pluginId": "fire-damage", "kernel": "FireDamageKernel" },
    "functions": [
      {
        "id": "ShouldHandle",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "e_DamageType", "type": "String" },
          { "name": "e_Amount", "type": "I32" },
          { "name": "e_TargetId", "type": "String" }
        ],
        "returnType": "Bool",
        "body": [{ "op": "return", "value": { "bool": true } }]
      },
      {
        "id": "Handle",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "e_DamageType", "type": "String" },
          { "name": "e_Amount", "type": "I32" },
          { "name": "e_TargetId", "type": "String" }
        ],
        "returnType": "Unit",
        "body": [{ "op": "return", "value": { "unit": true } }]
      }
    ]
  }
}
```

The envelope must not contain executable assembly paths, raw DLL bytes, CLR type names used as execution targets, or other host-code loading instructions. The server imports the JSON Safe IR module, validates it, and then runs the IR through the selected runtime mode.
Generated SDK factories return in-memory `PluginPackage` values for local tooling. The upload path is
`PluginPackageJsonSerializer.Export(FireDamagePluginPackage.Create())` followed by server-side
`InstallJsonAsync` validation.

The plugin package manifest should include live state metadata.

Example:

```json
{
  "pluginId": "fire-damage",
  "contract": "IEventKernel<DamageEvent>",
  "mode": "auto",
  "effects": [
    "Cpu",
    "Alloc",
    "GameStateWrite",
    "Audit"
  ],
  "liveSettings": [
    {
      "name": "Enabled",
      "type": "bool",
      "defaultValue": true
    },
    {
      "name": "DamageType",
      "type": "string",
      "defaultValue": "fire"
    },
    {
      "name": "MinDamage",
      "type": "int",
      "defaultValue": 100,
      "min": 0,
      "max": 10000
    }
  ],
  "subscriptions": [
    {
      "event": "DamageEvent",
      "kernel": "FireDamageKernel"
    }
  ]
}
```

The manifest should allow the plugin server/admin UI to show:

```text
settings
defaults
ranges
requested effects
event subscriptions
recommended execution mode
```

---

# 15. Validation Rules

The original verifier model remains unchanged.

This addendum adds the following high-level validation rules.

## Live Settings

Live setting properties must be:

```text
Allowed:
  bool
  int
  long
  double
  string
```

Enums, additional numeric types, approved readonly value objects, and nullable
forms are extension points, not part of the current SDK contract.

Live setting properties must not be:

```text
Forbidden:
  arbitrary object
  Type
  Delegate
  Stream
  IServiceProvider
  DbContext
  raw server entities
  mutable collections, unless explicitly supported
  reflection-capable types
```

## Hook Filters

Hook filters must only use:

```text
event/view data
hook context data
live bindings
kernel settings
approved helper calls
approved operators
```

Hook filters must not perform:

```text
file IO
network IO
database access
server mutation
async work
threading
unbounded allocation
non-deterministic hidden side effects
```

## Kernel Handlers

Kernel handlers may perform only actions allowed by their contract and granted capabilities.

Example:

```text
IItemFilter:
  Cpu only
  no mutation

IEventKernel<DamageEvent>:
  Cpu
  optional message send
  optional game-state action, if granted
```

---

# 16. Local Tooling Expectations

The plugin SDK should provide local diagnostics.

Invalid example:

```csharp
public bool ShouldHandle(DamageEvent e, HookContext ctx)
{
    File.WriteAllText("x.txt", "bad");
    return true;
}
```

Expected diagnostic:

```text
SGP001: Forbidden host API 'System.IO.File' is not allowed in this plugin contract.

Contract:
  IEventKernel<DamageEvent>.ShouldHandle

Allowed:
  event reads
  context reads
  live settings
  approved pure helpers

Forbidden:
  file access
  network access
  process access
  reflection
```

Live setting validation example:

```csharp
[LiveSetting]
public object Anything { get; set; }
```

Expected diagnostic:

```text
SGP020: Live setting type 'object' is not supported.

Use one of:
  bool
  int
  long
  double
  string
```

The server must still re-validate the uploaded package.

Local tooling is a developer-experience feature, not the final trust boundary.

---

# 17. Recommended Documentation Examples

The public documentation should introduce the system in this order:

1. implement a simple filter
2. implement a kernel
3. add live settings
4. register a hook
5. update settings at runtime
6. inspect plugin permissions
7. upload/install package

Recommended flagship example:

```csharp
[GamePlugin("fire-damage")]
public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
{
    [LiveSetting]
    public string DamageType { get; set; } = "fire";

    [LiveSetting]
    [Range(0, 10_000)]
    public int MinDamage { get; set; } = 100;

    public bool ShouldHandle(DamageEvent e, HookContext ctx)
    {
        return e.DamageType == DamageType
            && e.Amount >= MinDamage;
    }

    public void Handle(DamageEvent e, HookContext ctx)
    {
        ctx.Messages.Send(e.TargetId, "Ouch, fire.");
    }
}
```

Registration:

```csharp
server.RegisterEventAdapter(DamageEventAdapter.Instance);

server.Hooks.On<DamageEvent>()
    .UseKernel<FireDamageKernel>();
```

Runtime update:

```csharp
var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

kernel.Value.MinDamage = 250;
```

Simple mental model:

```text
Kernel properties are live settings.
ShouldHandle filters events.
Handle performs approved actions.
```

---

# 18. Design Guidance

Prefer this:

```csharp
public sealed partial class MyKernel : IEventKernel<MyEvent>
{
    [LiveSetting]
    public int Threshold { get; set; } = 100;

    public bool ShouldHandle(MyEvent e, HookContext ctx)
    {
        return e.Value > Threshold;
    }

    public void Handle(MyEvent e, HookContext ctx)
    {
        // handle
    }
}
```

Avoid making users write this frequently:

```csharp
context.GetBindingValue(binding).SomeValue
```

The latter is explicit but noisy.

The preferred ergonomics are:

```text
small scripts:
  BindValue<T>

medium scripts:
  BindContext<TSettings>

real plugins:
  Kernel properties
```

---

# 19. Non-Goals

This addendum does not make arbitrary C# safe.

This addendum does not allow plugin DLLs to be executed directly.

This addendum does not replace server-side verification.

This addendum does not require all hooks to be compiled.

This addendum does not require live setting changes to recompile the plugin.

This addendum does not specify the internal IR format.

This addendum does not specify the internal IL generation strategy.

This addendum is focused on the plugin developer and plugin server experience.

---

# 20. Summary

This addendum adds a higher-level live plugin model to the original Safe IR Sandbox design.

The key concepts are:

```text
shared contracts
kernel classes
live settings
value bindings
context bindings
server-side hook pipelines
interpreted / compiled / auto execution
```

The recommended product story is:

```text
Plugin developers implement server-provided interfaces.

Kernel properties are live settings.

ShouldHandle is the fast server-side filter.

Handle is the approved action path.

The server can update settings at runtime.

Future events use the latest settings.

The production server never executes arbitrary plugin DLLs; it installs validated Safe IR package data.
```

This gives the system a more ergonomic, game/server-friendly plugin model while preserving the original security and execution model.
