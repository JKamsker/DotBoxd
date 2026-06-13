# Safe-IR

Safe-IR is a restricted IR sandbox for .NET. User-authored work is represented as JSON IR, imported into a safe IR model, validated against a capability policy, and then executed either by the IR interpreter or by compiler-owned runtime forms.

Interpreted mode executes verified IR directly. Compiled mode is only a runtime optimization: the current compiler emits a verified generated assembly and the CLR executes that loaded form. `DynamicMethod` is reserved for a future backend after an equivalent gate exists. User input never supplies C#, raw IL, CLR member names, assemblies, or arbitrary host calls.

## Current Packages

- `SafeIR.Core`: IR model, policy model, resource metering, canonical hashing.
- `SafeIR.Validation`: structural, type, effect, policy, and binding validation. `ModuleValidator`
  returns the public `ModuleValidationResult` evidence shape with diagnostics, function analysis,
  module effects, required capabilities, and binding references.
- `SafeIR.Runtime`: safe host bindings for files, time, random, logging, strings, and math.
- `SafeIR.Serialization.Json`: JSON IR importer and exporter, host import extensions, and plugin package JSON upload helpers.
- `SafeIR.Transport.Http`: HTTP GET binding, grant helpers, pinned transport, and HTTP grant validation.
- `SafeIR.Transport.Ipc.ShaRpc`: preview MessagePack IPC addon built on ShaRPC generic transports, with named-pipe convenience helpers.
- `SafeIR.Interpreter`: direct IR execution backend.
- `SafeIR.Compiler`: generated-runtime backend and persistent artifact cache.
- `SafeIR.Verifier`: generated assembly verifier.
- `SafeIR.Hosting`: host-facing orchestration API.
- `SafeIR.PluginAnalyzer`: source generator and analyzer for local plugin packages.
- `SafeIR.Server.Abstractions`: purpose-agnostic plugin-to-host contracts a plugin author compiles
  against — `[Plugin]`, `IEventKernel<TEvent>`, `HookContext`, `IPluginMessageSink`,
  `IPluginEventAdapter<TEvent>`, and `LiveSettingAttribute`. Depends only on `SafeIR.Core`.
- `SafeIR.Plugins`: the host/server runtime that loads, validates, and dispatches plugins — plugin
  manifest, installed kernel, hook, message-binding, and plugin-package JSON APIs. Runtime
  package-install, prepared-package, kernel-entrypoint, and live-setting rejections surface stable
  `SGP*` diagnostics catalogued by the public `PluginDiagnosticCodes` reference (see
  [Plugin Runtime Diagnostics](#plugin-runtime-diagnostics)).

## Installing from NuGet

Use the package set that matches the host surface you are compiling against:

```powershell
# Minimal host execution with JSON import and safe runtime bindings.
dotnet add package SafeIR.Hosting
dotnet add package SafeIR.Runtime
dotnet add package SafeIR.Serialization.Json

# HTTP GET transport and policy helpers.
dotnet add package SafeIR.Transport.Http

# Plugin manifests/kernels plus production JSON upload helpers.
dotnet add package SafeIR.Plugins
dotnet add package SafeIR.Serialization.Json

# Source-generated plugin package factories.
dotnet add package SafeIR.PluginAnalyzer

# Preview IPC addon. This package currently follows a prerelease channel while ShaRPC dependencies are prerelease.
dotnet add package SafeIR.Transport.Ipc.ShaRpc --prerelease
```

Common namespaces:

- `SafeIR`, `SafeIR.Hosting`, and `SafeIR.Runtime` for host setup and execution.
- `SafeIR.Serialization.Json` for `ImportJsonAsync`, `SafeIrJsonImporter`, and `SafeIrJsonExporter` (the module export side of the JSON IR round trip).
- `SafeIR.Transport.Http` for HTTP binding registration and `GrantHttpGet`.
- `SafeIR.Server.Abstractions` for the plugin authoring contracts (`[Plugin]`,
  `IEventKernel<TEvent>`, `HookContext`).
- `SafeIR.Plugins` for plugin manifests, `PluginPackage`, and `PluginPackageJsonSerializer` (the
  production JSON plugin upload/import and export helper now lives in this package, which references
  `SafeIR.Serialization.Json` for the module-IR round trip).
- `SafeIR.Transport.Ipc` for the preview ShaRPC MessagePack IPC addon.

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

## JSON IR Round Trip

Tooling that builds or transforms `SandboxModule` instances can serialize them back to JSON IR
with `SafeIrJsonExporter` and re-import the result with `SafeIrJsonImporter`, both from the
`SafeIR.Serialization.Json` package:

```csharp
using SafeIR;
using SafeIR.Serialization.Json;

var json = SafeIrJsonExporter.Export(module, indented: true);
var roundTripped = SafeIrJsonImporter.Import(json);
var plan = await host.PrepareAsync(roundTripped, policy);
```

## JSON Ingestion Schemas

The public JSON ingestion envelopes ship with versioned, machine-readable JSON Schema artifacts so
plugin authors, admin UIs, and upload validators can validate JSON before sending it to a server
instead of inferring the contract from importer source:

- [`schemas/v1/safe-ir-module.schema.json`](schemas/v1/safe-ir-module.schema.json) describes the
  module envelope accepted by `SafeIrJsonImporter.Import(string)`.
- [`schemas/v1/safe-ir-plugin-package.schema.json`](schemas/v1/safe-ir-plugin-package.schema.json)
  describes the plugin package envelope accepted by `PluginPackageJsonSerializer.Import(string)`.

The module schema is embedded in the `SafeIR.Serialization.Json` package and exposed through
`SafeIrJsonSchemas.ModuleEnvelope` and `SafeIrJsonSchemas.SchemaVersion`; the plugin-package schema
is embedded in the `SafeIR.Plugins` package and exposed through
`PluginPackageJsonSchemas.PackageEnvelope`. The schemas are kept in sync with the importer's strict
shape by a regression test; the `v1` directory segment and `SchemaVersion` are bumped together
whenever the JSON contract changes.

## Local Verification

```powershell
dotnet restore SafeIR.slnx --locked-mode
dotnet build SafeIR.slnx --configuration Release --no-restore
dotnet test SafeIR.slnx --configuration Release --no-build
.\scripts\run-required-tests.ps1 `
  -Project tests\SafeIR.Tests\SafeIR.Tests.csproj `
  -Configuration Release `
  -NoBuild `
  -RequiredFullyQualifiedNameContains @(
    "SafeFileSystemTests",
    "SafeFileSystemReparsePointTests",
    "FileExtensionPolicyTests",
    "PathUriLiteralValidationTests",
    "CompiledArtifactGuardTests",
    "CompiledRuntimeQuotaTests",
    "VerifierAttackMatrixTests",
    "VerifierLoopMeteringTests",
    "BindingRegistryHardeningTests",
    "PluginPackageValidationTests",
    "PluginRevocationTests",
    "PinnedHttpTransportTests",
    "DifferentialFuzzTests"
  )
.\scripts\check-docs-smoke.ps1 -Configuration Release
.\scripts\check-csharp-file-lines.ps1
.\scripts\check-spec-manifest.ps1
.\scripts\check-release-readiness.ps1
Remove-Item artifacts\packages\*.nupkg -Force -ErrorAction SilentlyContinue
dotnet pack SafeIR.slnx --configuration Release --no-build --output artifacts/packages
.\scripts\check-package-metadata.ps1 -PackageDirectory artifacts\packages -AllowPrereleaseVersions
.\scripts\check-package-consumer-smoke.ps1 -PackageDirectory artifacts\packages -Configuration Release
```

`SafeIR.Transport.Ipc.ShaRpc` is intentionally packed as a prerelease package while its upstream ShaRPC dependencies are prerelease-only. Stable release gates fail if this preview addon is included in a stable package set before its package version and dependencies are stable.

CI builds and tests on Windows, Ubuntu, and macOS, but NuGet packages are produced only by the
canonical `ubuntu-latest` matrix leg and uploaded as `packages-canonical`. Treat that canonical
artifact set as the only publishable package output for a release.

## HTTP Transport Example

`examples/HttpTransport` is the maintained safe-setup path for the `SafeIR.Transport.Http`
package. It registers `AddNetworkBindings(...)` with a deterministic in-memory invoker, grants a
single host through `GrantHttpGet(...)` with explicit response-byte and timeout limits, runs a
module that calls `net.http.get`, and proves both an allowed request and a denied out-of-allowlist
request. Allowlist semantics, byte limits, timeout capping, and private-network defaults match the
production pinned transport; only the invoker is swapped for determinism.

```powershell
dotnet run --project examples\HttpTransport\SafeIR.HttpTransportExample\SafeIR.HttpTransportExample.csproj
```

## Logging Example

Logging is a user-facing safe API with an explicit capability boundary: a host must both register
the bindings at setup with `AddLogBindings()` and grant `log.write` in the policy with
`GrantLogging()`. Without both, a module that calls `log.info`/`log.warn` is denied with the
`E-POLICY-CAP` diagnostic. Granted log messages are audited as sanitized `SandboxLog` events
(secret-shaped tokens are redacted before they reach the sink), and `ResourceUsage.LogEvents`
reports how many log calls ran. `WithMaxLogEvents` and `WithMaxLogMessageLength` are the quota
controls; exceeding either returns a `QuotaExceeded` failure.

```csharp
using SafeIR;
using SafeIR.Hosting;
using SafeIR.Runtime;
using SafeIR.Serialization.Json;

using var host = SandboxHost.Create(builder => {
    builder.AddDefaultPureBindings();
    builder.AddLogBindings();
    builder.UseInterpreter();
});

var module = await host.ImportJsonAsync(loggingJsonIr);
var policy = SandboxPolicyBuilder.Create()
    .GrantLogging()
    .WithMaxLogEvents(8)
    .WithMaxLogMessageLength(256)
    .Build();

var plan = await host.PrepareAsync(module, policy);
var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
// result.ResourceUsage.LogEvents and the sanitized "SandboxLog" audit events expose the output.
```

The runnable standalone walkthrough lives in
`examples/Capabilities/SafeIR.Example.Capabilities/Examples/SafeLoggingExample.cs` and is exercised by the
docs smoke. It runs the granted path and a tight `WithMaxLogEvents` quota denial:

```powershell
dotnet run --project examples\Capabilities\SafeIR.Example.Capabilities\SafeIR.Example.Capabilities.csproj
```

## Plugin Addendum Examples

The addendum implementation lives in `src/SafeIR.Plugins`.

The addendum examples are split into three topic projects:

```powershell
dotnet run --project examples\Capabilities\SafeIR.Example.Capabilities\SafeIR.Example.Capabilities.csproj
dotnet run --project examples\Hosting\SafeIR.Example.Hosting\SafeIR.Example.Hosting.csproj
dotnet run --project examples\PluginAuthoring\SafeIR.Example.PluginAuthoring\SafeIR.Example.PluginAuthoring.csproj
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

## Game Server Plugin Example (golden)

The golden example runs an aggro/combat simulation server that exposes hooks and events, and a
separate plugin host that authors kernels, previews them locally, ships them as opaque verified IR
over IPC, and tunes live settings. The server runs the untrusted kernels sandboxed; they change game
behavior only through an example-defined command sink (the plugin's sole sandbox capability stays
`host.message.write`, while the game semantics live in the example, not in core). The server
self-launches the plugin host child process, so a single command drives the whole demo: it prints a
baseline phase where monsters bully low-level players, the host's local preview and ship/settings
logs, and a with-plugin phase where guardian/retaliation kernels keep the weak players alive.

```powershell
dotnet run --project examples\GameServer\SafeIR.Game.Server\SafeIR.Game.Server.csproj
```

## Plugin Runtime Diagnostics

The `SafeIR.Plugins` package emits stable `SGP*` `SandboxDiagnostic` codes when an uploaded or
generated plugin package is rejected. These runtime diagnostics are distinct from the compile-time
`SafeIR.PluginAnalyzer` SDK diagnostics (which share the `SGP` namespace) and from verifier `V-*`
diagnostics. The public `PluginDiagnosticCodes` reference in the `SafeIR.Plugins` namespace
catalogues every runtime `SGP*` code with its emitting phase, the audience that must fix it
(plugin author vs. host operator), the likely cause, and a remediation note:

```csharp
using SafeIR.Plugins;

try
{
    pluginServer.Install(package);
}
catch (SandboxValidationException ex)
{
    foreach (var diagnostic in ex.Diagnostics)
    {
        if (PluginDiagnosticCodes.TryGetReference(diagnostic.Code, out var reference))
        {
            // reference.Phase, reference.Audience, reference.Meaning, reference.Remediation
            // give upload UIs and hosts triage guidance instead of an opaque code.
        }
    }
}
```

| Code | Phase | Audience | Meaning |
|------|-------|----------|---------|
| SGP010 | Package validation | Plugin author | Manifest does not declare a plugin id. |
| SGP011 | Package validation | Plugin author | Manifest plugin id does not match the module id. |
| SGP012 | Package validation | Plugin author | Module metadata does not bind to the manifest plugin id. |
| SGP013 | Package validation | Plugin author | Module kernel metadata is missing or a subscription targets a different kernel. |
| SGP014 | Prepared-package validation | Plugin author | Contract is not a valid `IEventKernel<TEvent>` or its event does not match a subscription. |
| SGP020 | Live setting | Plugin author | Live setting type is unsupported or its default value is invalid. |
| SGP021 | Package validation | Plugin author | A live setting name is declared more than once. |
| SGP022 | Live setting | Plugin author | A range is declared on a non-numeric live setting type. |
| SGP023 | Live setting | Host operator | A live setting value is outside its allowed range. |
| SGP024 | Live setting | Plugin author | A live setting minimum is greater than its maximum. |
| SGP030 | Package validation | Plugin author | The manifest declares no hook subscriptions. |
| SGP031 | Package validation | Plugin author | A subscription is missing event/kernel, or a kernel is wired to an unsubscribed event. |
| SGP032 | Package validation | Plugin author | A required kernel entrypoint is missing or not public. |
| SGP033 | Prepared-package validation | Plugin author | An entrypoint signature does not match the hook event and live settings. |
| SGP034 | Prepared-package validation | Plugin author | Entrypoints disagree on parameter shape, or a pipeline uses a conflicting adapter. |
| SGP035 | Prepared-package validation | Plugin author | Live settings are not declared as trailing entrypoint parameters. |
| SGP040 | Package validation | Plugin author | An effect is unsupported or no verified effects are declared. |
| SGP041 | Prepared-package validation | Plugin author | Manifest effects do not match the verified entrypoint effects. |
| SGP042 | Package validation | Plugin author | The manifest execution mode is unsupported. |
| SGP050 | Package validation | Plugin author | Manifest text is empty, has control characters, or looks like a forbidden CLR/IL descriptor. |

`PluginDiagnosticCodes.All` is the maintained source of truth; a regression test fails if the
runtime emits an `SGP*` code that the reference does not document, so new runtime plugin
diagnostics cannot ship without user-facing guidance.
