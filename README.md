# Safe-IR

Safe-IR is a restricted IR sandbox for .NET. User-authored work is represented as JSON IR, imported into a safe IR model, validated against a capability policy, and then executed either by the IR interpreter or by compiler-owned runtime forms.

Interpreted mode executes verified IR directly. Compiled mode is only a runtime optimization: the current compiler emits a verified generated assembly and the CLR executes that loaded form. `DynamicMethod` is reserved for a future backend after an equivalent gate exists. User input never supplies C#, raw IL, CLR member names, assemblies, or arbitrary host calls.

## Current Packages

- `DotBoxd.Kernels`: IR model, policy model, resource metering, canonical hashing.
- `DotBoxd.Kernels.Validation`: structural, type, effect, policy, and binding validation. `ModuleValidator`
  returns the public `ModuleValidationResult` evidence shape with diagnostics, function analysis,
  module effects, required capabilities, and binding references.
- `DotBoxd.Kernels.Runtime`: safe host bindings for files, time, random, logging, strings, and math.
- `DotBoxd.Kernels.Serialization.Json`: JSON IR importer and exporter, host import extensions, and plugin package JSON upload helpers.
- `DotBoxd.Hosting.Http`: HTTP GET binding, grant helpers, pinned transport, and HTTP grant validation.
- `DotBoxd.Pushdown.Services`: preview MessagePack IPC addon built on DotBoxd generic transports, with named-pipe convenience helpers.
- `DotBoxd.Kernels.Interpreter`: direct IR execution backend.
- `DotBoxd.Kernels.Compiler`: generated-runtime backend and persistent artifact cache.
- `DotBoxd.Kernels.Verifier`: generated assembly verifier.
- `DotBoxd.Hosting`: host-facing orchestration API.
- `DotBoxd.Plugins.Analyzer`: source generator and analyzer for local plugin packages.
- `DotBoxd.Abstractions`: purpose-agnostic plugin-to-host contracts a plugin author compiles
  against — `[Plugin]`, `IEventKernel<TEvent>`, `HookContext`, `IPluginMessageSink`,
  `IPluginEventAdapter<TEvent>`, and `LiveSettingAttribute`. Depends only on `DotBoxd.Kernels`.
- `DotBoxd.Plugins`: the host/server runtime that loads, validates, and dispatches plugins — plugin
  manifest, installed kernel, hook, message-binding, and plugin-package JSON APIs. Runtime
  package-install, prepared-package, kernel-entrypoint, and live-setting rejections surface stable
  `SGP*` diagnostics catalogued by the public `PluginDiagnosticCodes` reference (see
  [Plugin Runtime Diagnostics](#plugin-runtime-diagnostics)).

## Installing from NuGet

Use the package set that matches the host surface you are compiling against:

```powershell
# Minimal host execution with JSON import and safe runtime bindings.
dotnet add package DotBoxd.Hosting
dotnet add package DotBoxd.Kernels.Runtime
dotnet add package DotBoxd.Kernels.Serialization.Json

# HTTP GET transport and policy helpers.
dotnet add package DotBoxd.Hosting.Http

# Plugin manifests/kernels plus production JSON upload helpers.
dotnet add package DotBoxd.Plugins
dotnet add package DotBoxd.Kernels.Serialization.Json

# Source-generated plugin package factories.
dotnet add package DotBoxd.Plugins.Analyzer

# Preview IPC addon. This package currently follows a prerelease channel while DotBoxd dependencies are prerelease.
dotnet add package DotBoxd.Pushdown.Services --prerelease
```

Common namespaces:

- `DotBoxd.Kernels`, `DotBoxd.Hosting`, and `DotBoxd.Kernels.Runtime` for host setup and execution.
- `DotBoxd.Kernels.Serialization.Json` for `ImportJsonAsync`, `DotBoxdJsonImporter`, and `DotBoxdJsonExporter` (the module export side of the JSON IR round trip).
- `DotBoxd.Hosting.Http` for HTTP binding registration and `GrantHttpGet`.
- `DotBoxd.Abstractions` for the plugin authoring contracts (`[Plugin]`,
  `IEventKernel<TEvent>`, `HookContext`).
- `DotBoxd.Plugins` for plugin manifests, `PluginPackage`, and `PluginPackageJsonSerializer` (the
  production JSON plugin upload/import and export helper now lives in this package, which references
  `DotBoxd.Kernels.Serialization.Json` for the module-IR round trip).
- `DotBoxd.Kernels.Transport.Ipc` for the preview DotBoxd MessagePack IPC addon.

## Minimal Host Usage

```csharp
using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;

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
with `DotBoxdJsonExporter` and re-import the result with `DotBoxdJsonImporter`, both from the
`DotBoxd.Kernels.Serialization.Json` package:

```csharp
using DotBoxd.Kernels;
using DotBoxd.Kernels.Serialization.Json;

var json = DotBoxdJsonExporter.Export(module, indented: true);
var roundTripped = DotBoxdJsonImporter.Import(json);
var plan = await host.PrepareAsync(roundTripped, policy);
```

## JSON Ingestion Schemas

The public JSON ingestion envelopes ship with versioned, machine-readable JSON Schema artifacts so
plugin authors, admin UIs, and upload validators can validate JSON before sending it to a server
instead of inferring the contract from importer source:

- [`schemas/v1/dotboxd-kernel-module.schema.json`](schemas/v1/dotboxd-kernel-module.schema.json) describes the
  module envelope accepted by `DotBoxdJsonImporter.Import(string)`.
- [`schemas/v1/dotboxd-plugin-package.schema.json`](schemas/v1/dotboxd-plugin-package.schema.json)
  describes the plugin package envelope accepted by `PluginPackageJsonSerializer.Import(string)`.

The module schema is embedded in the `DotBoxd.Kernels.Serialization.Json` package and exposed through
`DotBoxdJsonSchemas.ModuleEnvelope` and `DotBoxdJsonSchemas.SchemaVersion`; the plugin-package schema
is embedded in the `DotBoxd.Plugins` package and exposed through
`PluginPackageJsonSchemas.PackageEnvelope`. The schemas are kept in sync with the importer's strict
shape by a regression test; the `v1` directory segment and `SchemaVersion` are bumped together
whenever the JSON contract changes.

## Local Verification

```powershell
dotnet restore DotBoxd.Kernels.slnx --locked-mode
dotnet build DotBoxd.Kernels.slnx --configuration Release --no-restore
dotnet test DotBoxd.Kernels.slnx --configuration Release --no-build
.\scripts\run-required-tests.ps1 `
  -Project tests\DotBoxd.Kernels.Tests\DotBoxd.Kernels.Tests.csproj `
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
dotnet pack DotBoxd.Kernels.slnx --configuration Release --no-build --output artifacts/packages
.\scripts\check-package-metadata.ps1 -PackageDirectory artifacts\packages -AllowPrereleaseVersions
.\scripts\check-package-consumer-smoke.ps1 -PackageDirectory artifacts\packages -Configuration Release
```

`DotBoxd.Pushdown.Services` is intentionally packed as a prerelease package while its upstream DotBoxd dependencies are prerelease-only. Stable release gates fail if this preview addon is included in a stable package set before its package version and dependencies are stable.

CI builds and tests on Windows, Ubuntu, and macOS, but NuGet packages are produced only by the
canonical `ubuntu-latest` matrix leg and uploaded as `packages-canonical`. Treat that canonical
artifact set as the only publishable package output for a release.

## HTTP Transport Example

`examples/HttpTransport` is the maintained safe-setup path for the `DotBoxd.Hosting.Http`
package. It registers `AddNetworkBindings(...)` with a deterministic in-memory invoker, grants a
single host through `GrantHttpGet(...)` with explicit response-byte and timeout limits, runs a
module that calls `net.http.get`, and proves both an allowed request and a denied out-of-allowlist
request. Allowlist semantics, byte limits, timeout capping, and private-network defaults match the
production pinned transport; only the invoker is swapped for determinism.

```powershell
dotnet run --project examples\HttpTransport\DotBoxd.Kernels.HttpTransportExample\DotBoxd.Kernels.HttpTransportExample.csproj
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
using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;
using DotBoxd.Kernels.Serialization.Json;

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
`examples/Capabilities/DotBoxd.Kernels.Example.Capabilities/Examples/SafeLoggingExample.cs` and is exercised by the
docs smoke. It runs the granted path and a tight `WithMaxLogEvents` quota denial:

```powershell
dotnet run --project examples\Capabilities\DotBoxd.Kernels.Example.Capabilities\DotBoxd.Kernels.Example.Capabilities.csproj
```

## Plugin Addendum Examples

The addendum implementation lives in `src/DotBoxd.Plugins`.

The addendum examples are split into three topic projects:

```powershell
dotnet run --project examples\Capabilities\DotBoxd.Kernels.Example.Capabilities\DotBoxd.Kernels.Example.Capabilities.csproj
dotnet run --project examples\Hosting\DotBoxd.Kernels.Example.Hosting\DotBoxd.Kernels.Example.Hosting.csproj
dotnet run --project examples\PluginAuthoring\DotBoxd.Kernels.Example.PluginAuthoring\DotBoxd.Kernels.Example.PluginAuthoring.csproj
```

Run the local live-kernel example:

```powershell
dotnet run --project examples\LocalPlugin\DotBoxd.Kernels.PluginLocal\DotBoxd.Kernels.PluginLocal.csproj
```

Run the real named-pipe IPC sample with the DotBoxd MessagePack addon:

Terminal 1:

```powershell
dotnet run --project examples\PluginIpc\DotBoxd.Kernels.PluginIpc.Server\DotBoxd.Kernels.PluginIpc.Server.csproj -- dotboxd-plugin-ipc-local-demo
```

Terminal 2:

```powershell
dotnet run --project examples\PluginIpc\DotBoxd.Kernels.PluginIpc.Client\DotBoxd.Kernels.PluginIpc.Client.csproj -- dotboxd-plugin-ipc-local-demo
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
dotnet run --project examples\GameServer\DotBoxd.Kernels.Game.Server\DotBoxd.Kernels.Game.Server.csproj
```

## Plugin Runtime Diagnostics

The `DotBoxd.Plugins` package emits stable `SGP*` `SandboxDiagnostic` codes when an uploaded or
generated plugin package is rejected. These runtime diagnostics are distinct from the compile-time
`DotBoxd.Plugins.Analyzer` SDK diagnostics (which share the `SGP` namespace) and from verifier `V-*`
diagnostics. The public `PluginDiagnosticCodes` reference in the `DotBoxd.Plugins` namespace
catalogues every runtime `SGP*` code with its emitting phase, the audience that must fix it
(plugin author vs. host operator), the likely cause, and a remediation note:

```csharp
using DotBoxd.Plugins;

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
| DBXK010 | Package validation | Plugin author | Manifest does not declare a plugin id. |
| DBXK011 | Package validation | Plugin author | Manifest plugin id does not match the module id. |
| DBXK012 | Package validation | Plugin author | Module metadata does not bind to the manifest plugin id. |
| DBXK013 | Package validation | Plugin author | Module kernel metadata is missing or a subscription targets a different kernel. |
| DBXK014 | Prepared-package validation | Plugin author | Contract is not a valid `IEventKernel<TEvent>` or its event does not match a subscription. |
| DBXK020 | Live setting | Plugin author | Live setting type is unsupported or its default value is invalid. |
| DBXK021 | Package validation | Plugin author | A live setting name is declared more than once. |
| DBXK022 | Live setting | Plugin author | A range is declared on a non-numeric live setting type. |
| DBXK023 | Live setting | Host operator | A live setting value is outside its allowed range. |
| DBXK024 | Live setting | Plugin author | A live setting minimum is greater than its maximum. |
| DBXK030 | Package validation | Plugin author | The manifest declares no hook subscriptions. |
| DBXK031 | Package validation | Plugin author | A subscription is missing event/kernel, or a kernel is wired to an unsubscribed event. |
| DBXK032 | Package validation | Plugin author | A required kernel entrypoint is missing or not public. |
| DBXK033 | Prepared-package validation | Plugin author | An entrypoint signature does not match the hook event and live settings. |
| DBXK034 | Prepared-package validation | Plugin author | Entrypoints disagree on parameter shape, or a pipeline uses a conflicting adapter. |
| DBXK035 | Prepared-package validation | Plugin author | Live settings are not declared as trailing entrypoint parameters. |
| DBXK040 | Package validation | Plugin author | An effect is unsupported or no verified effects are declared. |
| DBXK041 | Prepared-package validation | Plugin author | Manifest effects do not match the verified entrypoint effects. |
| DBXK042 | Package validation | Plugin author | The manifest execution mode is unsupported. |
| DBXK050 | Package validation | Plugin author | Manifest text is empty, has control characters, or looks like a forbidden CLR/IL descriptor. |

`PluginDiagnosticCodes.All` is the maintained source of truth; a regression test fails if the
runtime emits an `SGP*` code that the reference does not document, so new runtime plugin
diagnostics cannot ship without user-facing guidance.
