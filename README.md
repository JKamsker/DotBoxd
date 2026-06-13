# Safe-IR

Safe-IR is a restricted IR sandbox for .NET. User-authored work is represented as JSON IR, imported into a safe IR model, validated against a capability policy, and then executed either by the IR interpreter or by compiler-owned runtime forms.

Interpreted mode executes verified IR directly. Compiled mode is only a runtime optimization: the current compiler emits a verified generated assembly and the CLR executes that loaded form. `DynamicMethod` is reserved for a future backend after an equivalent gate exists. User input never supplies C#, raw IL, CLR member names, assemblies, or arbitrary host calls.

## Current Packages

- `SafeIR.Core`: IR model, policy model, resource metering, canonical hashing.
- `SafeIR.Validation`: structural, type, effect, policy, and binding validation. `ModuleValidator`
  returns the public `ModuleValidationResult` evidence shape with diagnostics, function analysis,
  module effects, required capabilities, and binding references.
- `SafeIR.Runtime`: safe host bindings for files, time, random, logging, strings, and math.
- `SafeIR.Serialization.Json`: JSON IR importer and host import extensions.
- `SafeIR.Transport.Http`: HTTP GET binding, grant helpers, pinned transport, and HTTP grant validation.
- `SafeIR.Transport.Ipc.ShaRpc`: preview MessagePack IPC addon built on ShaRPC generic transports, with named-pipe convenience helpers.
- `SafeIR.Interpreter`: direct IR execution backend.
- `SafeIR.Compiler`: generated-runtime backend and persistent artifact cache.
- `SafeIR.Verifier`: generated assembly verifier.
- `SafeIR.Hosting`: host-facing orchestration API.
- `SafeIR.PluginAnalyzer`: source generator and analyzer for local plugin packages.
- `SafeIR.Plugins`: live plugin manifest, hook, kernel, and message-binding APIs.

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
- `SafeIR.Serialization.Json` for `ImportJsonAsync` and `SafeIrJsonImporter`.
- `SafeIR.Transport.Http` for HTTP binding registration and `GrantHttpGet`.
- `SafeIR.Plugins` for plugin manifests, `PluginPackage`, and `PluginPackageJsonSerializer` upload/export helpers.
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
