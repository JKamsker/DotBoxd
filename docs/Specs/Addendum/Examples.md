# Addendum Implementation Examples

This branch implements the addendum as a hosting-layer plugin model:

- `SafeIR.Plugins` exposes live values, typed live contexts, kernel state, hook pipelines, plugin manifests, and safe message bindings.
- `SafeIR.PluginAnalyzer` provides local SDK diagnostics for forbidden File IO in kernels and unsupported live setting types.
- `SafeIR.PluginIpc.Server.Abstractions` owns the server-side event contracts that plugin clients implement against.
- Plugin packages carry JSON Safe IR plus manifest metadata. The server validates manifest identity, declared effects, and the Safe IR module with the existing Safe IR validator before installation.
- Hook handlers run through `SandboxHost.ExecuteAsync`. The local examples reference a generated package factory for trusted development-time convenience; production upload accepts serialized JSON package data and does not load arbitrary plugin DLLs.

## Recommended Documentation Walkthrough

The public documentation should lead with the kernel-class authoring model, then show how the server installs and runs the lowered Safe IR package. The snippets below follow the addendum's recommended order.

### 1. Implement A Simple Filter

Use simple filters for pure read-only decisions.

```csharp
public interface IItemFilter
{
    bool Accept(ItemView item, PlayerView player);
}

public sealed record ItemView(string Id, Rarity Rarity);

public sealed record PlayerView(string Id, int Level);

public enum Rarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

public sealed partial class EpicItemsOnly : IItemFilter
{
    public bool Accept(ItemView item, PlayerView player)
    {
        return item.Rarity >= Rarity.Epic;
    }
}
```

Simple filters request CPU-only execution. They do not mutate server state and do not receive raw host services.
This simple contract is host-side C# guidance today; the current SafeIR source generator only
lowers `IEventKernel<TEvent>` kernels into plugin packages.

### 2. Implement A Kernel

Use kernels when the plugin needs both a server-side filter and an approved action path. The current sample kernel lives at `examples\LocalPlugin\SafeIR.PluginLocal\FireDamageKernel.cs`.

```csharp
[GamePlugin("fire-damage")]
public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
{
    public bool ShouldHandle(DamageEvent e, HookContext ctx)
    {
        return e.DamageType == "fire" && e.Amount >= 100;
    }

    public void Handle(DamageEvent e, HookContext ctx)
    {
        ctx.Messages.Send(e.TargetId, "Ouch, fire.");
    }
}
```

`ShouldHandle` is the server-side filter. `Handle` can only perform actions exposed by the approved context facade.

### 3. Add Live Settings

Kernel properties become live settings when the generated package manifest exposes them as live state metadata. The authoring-side C# uses `[LiveSetting]`; the source generator mirrors the same settings into the manifest.

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
        return e.DamageType == DamageType &&
               e.Amount >= MinDamage;
    }

    public void Handle(DamageEvent e, HookContext ctx)
    {
        ctx.Messages.Send(e.TargetId, "Ouch, fire.");
    }
}
```

The generated sample manifest includes the matching metadata:

```csharp
new LiveSettingDefinition("DamageType", "string", "fire");
new LiveSettingDefinition("MinDamage", "int", 100, 0, 10_000);
```

The runtime enforces supported live setting types and numeric ranges during install and live updates.

### 4. Register A Hook

The server registers hooks against event adapters. Event adapters convert trusted host event snapshots into `SandboxValue` inputs for the verified IR entrypoints.

```csharp
var messages = new InMemoryPluginMessageSink();
var server = PluginServer.Create(messages);
await server.InstallAsync(FireDamagePluginPackage.Create());

server.Hooks.On<DamageEvent>()
    .UseKernel<FireDamageKernel>();
```

The pipeline flow is:

```text
DamageEvent
  -> server-side hook filters
  -> kernel.ShouldHandle
  -> kernel.Handle
```

### 5. Update Settings At Runtime

Live setting changes are applied to future hook executions without reinstalling the plugin or rebuilding the hook pipeline. Use `ModifyAsync` when multiple settings should become visible as one validated batch.

```csharp
var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

await kernel.ModifyAsync(state => {
    state.MinDamage = 250;
    state.DamageType = "ice";
});

await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-2"));
```

Pass `atomic: true` when the update must also wait for any in-flight kernel execution and prevent a new execution from crossing the commit boundary:

```csharp
await kernel.ModifyAsync(
    state => {
        state.MinDamage = 250;
        state.DamageType = "ice";
    },
    atomic: true);
```

Direct `kernel.Value` assignments use full synchronous synchronization by default. If a caller wants fire-and-forget direct assignments, set `UpdateMode` and flush only when an acknowledgement is needed:

```csharp
kernel.UpdateMode = LiveUpdateMode.AsyncSet;
kernel.Value.MinDamage = 250;

await kernel.FlushUpdatesAsync();
```

For small scripts, the same live-update behavior is available through value bindings:

```csharp
var minDamage = server.BindValue("minDamage", 100);

server.Hooks.On<DamageEvent>()
    .Where((e, _) => e.Amount >= minDamage.Value)
    .InvokeHostHandler((e, ctx) => ctx.Messages.Send(e.TargetId, "matched"));

minDamage.Value = 250;
```

For grouped settings, use a typed live context:

```csharp
var settings = server.BindContext<IFireDamageSettings>(
    "operatorDefaults",
    value => {
        value.DamageType = "fire";
        value.MinDamage = 100;
    });

settings.Value.MinDamage = 250;
```

Over IPC, send the same update as one request:

```csharp
await service.ModifySettingsAsync(
    [
        new LiveSettingUpdate("MinDamage", "250"),
        new LiveSettingUpdate("DamageType", "ice")
    ],
    atomic: true);
```

### 6. Inspect Plugin Permissions

The plugin package manifest exposes the permissions and subscriptions that an admin UI or server owner can inspect before enabling the plugin.

```csharp
var package = FireDamagePluginPackage.Create();

foreach (var effect in package.Manifest.Effects) {
    Console.WriteLine(effect);
}

foreach (var setting in package.Manifest.LiveSettings) {
    Console.WriteLine($"{setting.Name}: {setting.Type} = {setting.DefaultValue}");
}
```

The sample package requests:

```text
Effects:
  Cpu
  Alloc
  GameStateWrite
  Audit

Capability request:
  game.message.write

Subscription:
  DamageEvent -> FireDamageKernel
```

This is the data a server owner needs to show settings, defaults, ranges, requested effects, and hook subscriptions before install.

### 7. Upload Or Install Package

The production server installs a plugin package from serialized JSON package data. The JSON envelope contains a manifest, entrypoint names if needed, and the Safe IR module. It does not contain an assembly path or plugin DLL reference.

```csharp
var kernel = await server.InstallJsonAsync(uploadedPackageJson);
```

The local generated factory is still useful for SDK examples and tests, but it is not the production upload boundary:

```csharp
var package = FireDamagePluginPackage.Create();
var kernel = await server.InstallAsync(package);
```

The default plugin server policy grants the safe message capability:

```csharp
SandboxPolicyBuilder.Create()
    .GrantLogging()
    .GrantGameMessageWrite()
    .WithFuel(100_000)
    .WithMaxHostCalls(1_000)
    .Build();
```

If the policy does not grant `game.message.write`, package preparation fails closed with a policy diagnostic. The server still re-validates uploaded JSON packages; local analyzer diagnostics are developer-experience feedback, not the trust boundary.

## Flagship Fire Damage Example

The flagship example is implemented in:

- `examples\LocalPlugin\SafeIR.PluginLocal\FireDamageKernel.cs`
- `examples\PluginIpc\SafeIR.PluginIpc.Server.Abstractions\DamageEvent.cs`
- `SafeIR.PluginAnalyzer` generated `FireDamagePluginPackage.g.cs`
- `examples\LocalPlugin\SafeIR.PluginLocal\Program.cs`

Mental model:

```text
Kernel properties are live settings.
ShouldHandle filters events.
Handle performs approved actions.
The server executes verified Safe IR, not arbitrary plugin DLLs.
```

## Local Kernel Example

Run the complete addendum example set:

```powershell
dotnet run --project examples\Addendum\SafeIR.AddendumExamples\SafeIR.AddendumExamples.csproj
```

Run:

```powershell
dotnet run --project examples\LocalPlugin\SafeIR.PluginLocal\SafeIR.PluginLocal.csproj
```

The example installs the `fire-damage` kernel, publishes events, updates `MinDamage` and `DamageType` at runtime, and shows that future hook executions observe the latest live settings.

It also demonstrates:

- Level 1: host-authored `BindValue<T>`
- Level 2: host-authored `BindContext<TSettings>`
- Level 3: package/generator-backed IR kernel classes with manifest live settings

## Named-Pipe IPC Example

The IPC sample uses `SafeIR.Transport.Ipc.ShaRpc`, which wraps ShaRPC named pipes with MessagePack serialization. The pipe name is a trusted local control-plane endpoint: pass an explicit high-entropy or otherwise deployment-scoped name, and do not expose it across tenant boundaries. The shared contract project still references `ShaRPC` and `MessagePack` because service attributes and payload attributes live with the contract types.

Run the server in one terminal:

```powershell
dotnet run --project examples\PluginIpc\SafeIR.PluginIpc.Server\SafeIR.PluginIpc.Server.csproj -- safe-ir-plugin-ipc-local-demo
```

Run the client in another:

```powershell
dotnet run --project examples\PluginIpc\SafeIR.PluginIpc.Client\SafeIR.PluginIpc.Client.csproj -- safe-ir-plugin-ipc-local-demo
```

The client reads settings, publishes a matching event, changes live settings over IPC, and publishes again to prove the server-side hook pipeline uses the updated state.
