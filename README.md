# Safe-IR

Safe-IR is a JSON IR sandbox for .NET. User-authored work is represented as restricted JSON IR, validated against a capability policy, and then executed either by the IR interpreter or by compiler-owned runtime forms.

Interpreted mode executes verified IR directly. Compiled mode is only a runtime optimization: trusted compiler code emits a gated `DynamicMethod` or generated assembly, then the CLR executes that compiled form. User input never supplies C#, raw IL, CLR member names, assemblies, or arbitrary host calls.

## Current Packages

- `SafeIR.Core`: IR model, policy model, resource metering, JSON import, canonical hashing.
- `SafeIR.Validation`: structural, type, effect, policy, and binding validation.
- `SafeIR.Runtime`: safe host bindings for files, network, time, random, logging, strings, and math.
- `SafeIR.Interpreter`: direct IR execution backend.
- `SafeIR.Compiler`: generated-runtime backend and persistent artifact cache.
- `SafeIR.Verifier`: generated assembly verifier.
- `SafeIR.Hosting`: host-facing orchestration API.

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
