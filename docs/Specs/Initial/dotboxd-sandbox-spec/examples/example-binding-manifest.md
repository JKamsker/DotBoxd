# Example Binding Manifest

```json
{
  "manifestVersion": 1,
  "sandboxRuntimeVersion": "1.0.0",
  "bindings": [
    {
      "id": "math.sqrt",
      "version": "1.0.0",
      "parameters": ["F64"],
      "returnType": "F64",
      "effects": ["Cpu"],
      "requiredCapability": null,
      "costModel": {
        "baseFuel": 2
      },
      "auditLevel": "None",
      "safety": "PureIntrinsic",
      "compiledTarget": {
        "kind": "RuntimeStub",
        "type": "DotBoxD.Kernels.Runtime.CompiledRuntime",
        "method": "SqrtF64"
      }
    },
    {
      "id": "file.readText",
      "version": "1.0.0",
      "parameters": ["SandboxPath"],
      "returnType": "String",
      "effects": ["Cpu", "Alloc", "FileRead", "Concurrency"],
      "isAsync": true,
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
        "type": "DotBoxD.Kernels.Runtime.CompiledRuntime",
        "method": "CallBinding"
      }
    },
    {
      "id": "host.message.send",
      "version": "1.0.0",
      "parameters": ["String", "String"],
      "returnType": "Unit",
      "effects": ["Cpu", "Alloc", "HostStateWrite", "Concurrency", "Audit"],
      "isAsync": true,
      "requiredCapability": "host.message.write",
      "costModel": {
        "baseFuel": 5,
        "maxCallsPerRun": 100
      },
      "auditLevel": "PerCall",
      "safety": "SideEffectingExternal",
      "compiledTarget": {
        "kind": "RuntimeStub",
        "type": "DotBoxD.Kernels.Runtime.CompiledRuntime",
        "method": "CallBinding"
      }
    }
  ]
}
```

## Manifest hash

The normalized manifest is hashed and included in:

- execution plan hash
- compiled DLL cache key
- audit logs

Changing a binding's effects, signature, async flag, capability, safety level, or compiled target invalidates compiled artifacts.
