# Safe-IR

Safe-IR is a restricted IR sandbox for .NET. User-authored work is represented as JSON IR, imported into a safe IR model, validated against a capability policy, and then executed either by the IR interpreter or by compiler-owned runtime forms.

Interpreted mode executes verified IR directly. Compiled mode is only a runtime optimization: the current compiler emits a verified generated assembly and the CLR executes that loaded form. `DynamicMethod` is reserved for a future backend after an equivalent gate exists. User input never supplies C#, raw IL, CLR member names, assemblies, or arbitrary host calls.

## Current Packages

- `SafeIR.Core`: IR model, policy model, resource metering, canonical hashing.
- `SafeIR.Validation`: structural, type, effect, policy, and binding validation.
- `SafeIR.Runtime`: safe host bindings for files, time, random, logging, strings, and math.
- `SafeIR.Serialization.Json`: JSON IR importer and host import extensions.
- `SafeIR.Transport.Http`: HTTP GET binding, grant helpers, pinned transport, and HTTP grant validation.
- `SafeIR.Transport.Ipc.ShaRpc`: preview MessagePack named-pipe IPC helpers built on the prerelease ShaRPC NuGet packages.
- `SafeIR.Interpreter`: direct IR execution backend.
- `SafeIR.Compiler`: generated-runtime backend and persistent artifact cache.
- `SafeIR.Verifier`: generated assembly verifier.
- `SafeIR.Hosting`: host-facing orchestration API.
- `SafeIR.PluginAnalyzer`: source generator and analyzer for local plugin packages.
- `SafeIR.Plugins`: live plugin manifest, hook, kernel, and message-binding APIs.

## Minimal Host Usage

```csharp
using SafeIR;
using SafeIR.Hosting;
using SafeIR.Runtime;

var host = SandboxHost.Create(builder => {
    builder.AddDefaultPureBindings();
    builder.AddFileBindings();
    builder.UseInterpreter();
    builder.UseCompilerIfAvailable();
});

var module = await host.ImportJsonAsync(jsonIr);
var policy = SandboxPolicyBuilder.Create()
    .GrantFileRead(root: @"C:\tenant\123\config", maxBytesPerRun: 256_000)
    .WithFuel(10_000)
    .Build();

var plan = await host.PrepareAsync(module, policy);
var result = await host.ExecuteAsync(
    plan,
    entrypoint: "main",
    input: SandboxValue.Unit,
    options: new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
```

## JSON IR Example

```json
{
  "id": "config-reader",
  "version": "1.0.0",
  "capabilityRequests": [
    { "id": "file.read", "reason": "Read tenant-local config" }
  ],
  "functions": [
    {
      "id": "main",
      "visibility": "entrypoint",
      "parameters": [],
      "returnType": "String",
      "body": [
        {
          "op": "return",
          "value": {
            "call": "file.readText",
            "args": [{ "path": "settings.json" }]
          }
        }
      ]
    }
  ]
}
```

## Local Verification

```powershell
dotnet restore SafeIR.slnx
dotnet build SafeIR.slnx
dotnet test SafeIR.slnx
.\scripts\check-csharp-file-lines.ps1
dotnet pack SafeIR.slnx --configuration Release --output artifacts/packages
```

`SafeIR.Transport.Ipc.ShaRpc` is intentionally packed as a prerelease package while its upstream ShaRPC dependencies are prerelease-only. Stable release gates allow only that preview addon to carry prerelease metadata and dependencies.

## Plugin Addendum Examples

The addendum implementation lives in `src/SafeIR.Plugins`.

Run the complete addendum example set:

```powershell
dotnet run --project examples\Addendum\SafeIR.AddendumExamples\SafeIR.AddendumExamples.csproj
```

Run the local live-kernel example:

```powershell
dotnet run --project examples\LocalPlugin\SafeIR.PluginLocal\SafeIR.PluginLocal.csproj
```

Run the real named-pipe IPC sample with the ShaRPC MessagePack addon:

Terminal 1:

```powershell
dotnet run --project examples\PluginIpc\SafeIR.PluginIpc.Server\SafeIR.PluginIpc.Server.csproj -- safe-ir-plugin-ipc-local-demo
```

Terminal 2:

```powershell
dotnet run --project examples\PluginIpc\SafeIR.PluginIpc.Client\SafeIR.PluginIpc.Client.csproj -- safe-ir-plugin-ipc-local-demo
```

See `docs\Specs\Addendum\Examples.md` for details.
